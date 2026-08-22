using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// M5: on-demand body fetch (cached to a local file, never re-downloaded once cached), HTML
// rendered with remote images blocked by default, and read-state marking on open.
// M6: flag/delete(→Trash)/archive act immediately on local state, then queue the matching
// OutboxAction so the change survives being offline and replays once connectivity returns.
public sealed partial class ReadingPaneViewModel(
    IMessageStore messageStore,
    IFolderStore folderStore,
    IAccountStore accountStore,
    ICredentialStore credentialStore,
    IOutboxStore outboxStore,
    IOutboxProcessor outboxProcessor,
    Func<IImapAccountClient> imapClientFactory,
    AppDataPaths appDataPaths) : ViewModelBase
{
    private string? _rawHtml;

    [ObservableProperty]
    private MailMessage? _selectedMessage;

    [ObservableProperty]
    private string? _plainTextBody;

    [ObservableProperty]
    private string? _htmlBody;

    [ObservableProperty]
    private bool _isHtml;

    [ObservableProperty]
    private bool _hasBlockedRemoteImages;

    [ObservableProperty]
    private bool _remoteImagesAllowed;

    // Raised after any change to the open message's state (opened/flag toggled/moved) so the
    // message list can refresh the corresponding row (or remove it, if it moved elsewhere).
    public event EventHandler? MessageStateChanged;

    public async Task LoadAsync(MailMessage message)
    {
        SelectedMessage = message;
        ErrorMessage = null;
        PlainTextBody = null;
        HtmlBody = null;
        IsHtml = false;
        HasBlockedRemoteImages = false;
        RemoteImagesAllowed = false;
        _rawHtml = null;
        IsBusy = true;
        try
        {
            var (textBody, htmlBody) = message.IsBodyDownloaded
                ? await ReadCachedBodyAsync(message)
                : await FetchAndCacheBodyAsync(message);

            _rawHtml = htmlBody;
            if (htmlBody is not null)
            {
                IsHtml = true;
                ApplyHtmlRendering();
            }
            else
            {
                IsHtml = false;
                PlainTextBody = string.IsNullOrEmpty(textBody) ? "(本文がありません)" : textBody;
            }

            await MarkAsReadAsync(message);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"本文の取得に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            MessageStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void LoadRemoteImages()
    {
        RemoteImagesAllowed = true;
        ApplyHtmlRendering();
    }

    [RelayCommand]
    private async Task ToggleFlagAsync()
    {
        var message = SelectedMessage;
        if (message is null)
            return;

        var newFlagged = !message.IsFlagged;
        message.IsFlagged = newFlagged;
        await messageStore.SetFlaggedAsync(message.Id, newFlagged, CancellationToken.None);

        await EnqueueOutboxActionAsync(
            message.AccountId,
            newFlagged ? OutboxActionType.MarkFlagged : OutboxActionType.MarkUnflagged,
            message.Id,
            targetFolderId: null,
            new OutboxMessageRef(message.FolderId, message.Uid));

        MessageStateChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private Task DeleteAsync() => MoveToSpecialFolderAsync(MailFolderSpecialUse.Trash);

    [RelayCommand]
    private Task ArchiveAsync() => MoveToSpecialFolderAsync(MailFolderSpecialUse.Archive);

    private async Task MoveToSpecialFolderAsync(MailFolderSpecialUse targetUse)
    {
        var message = SelectedMessage;
        if (message is null)
            return;

        ErrorMessage = null;
        var accountFolders = await folderStore.GetByAccountAsync(message.AccountId, CancellationToken.None);
        var target = accountFolders.FirstOrDefault(f => f.SpecialUse == targetUse);
        if (target is null)
        {
            ErrorMessage = $"{DescribeSpecialUse(targetUse)}フォルダが見つかりません。";
            return;
        }
        if (target.Id == message.FolderId)
            return;

        var sourceRef = new OutboxMessageRef(message.FolderId, message.Uid);
        var sourceFolder = await folderStore.GetByIdAsync(message.FolderId, CancellationToken.None);

        var moved = WithFolder(message, target.Id);
        await messageStore.SaveAsync(moved, CancellationToken.None);

        if (sourceFolder is not null)
        {
            var newUnread = message.IsRead ? sourceFolder.UnreadCount : Math.Max(0, sourceFolder.UnreadCount - 1);
            await folderStore.UpdateCountsAsync(sourceFolder.Id, newUnread, Math.Max(0, sourceFolder.TotalCount - 1), CancellationToken.None);
        }

        await EnqueueOutboxActionAsync(message.AccountId, OutboxActionType.Move, message.Id, target.Id, sourceRef);

        SelectedMessage = null;
        PlainTextBody = null;
        HtmlBody = null;
        IsHtml = false;
        MessageStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task EnqueueOutboxActionAsync(
        Guid accountId, OutboxActionType type, Guid? messageId, Guid? targetFolderId, OutboxMessageRef reference)
    {
        var action = new OutboxAction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            MessageId = messageId,
            TargetFolderId = targetFolderId,
            PayloadJson = JsonSerializer.Serialize(reference),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await outboxStore.EnqueueAsync(action, CancellationToken.None);

        // Best-effort immediate replay; if offline this just no-ops and stays queued.
        _ = outboxProcessor.ProcessAsync(accountId, CancellationToken.None);
    }

    private static string DescribeSpecialUse(MailFolderSpecialUse use) => use switch
    {
        MailFolderSpecialUse.Trash => "ゴミ箱",
        MailFolderSpecialUse.Archive => "アーカイブ",
        _ => use.ToString(),
    };

    private static MailMessage WithFolder(MailMessage message, Guid folderId) => new()
    {
        Id = message.Id,
        AccountId = message.AccountId,
        FolderId = folderId,
        Uid = message.Uid,
        MessageId = message.MessageId,
        InReplyTo = message.InReplyTo,
        References = message.References,
        Subject = message.Subject,
        FromDisplay = message.FromDisplay,
        FromAddress = message.FromAddress,
        ToRecipients = message.ToRecipients,
        CcRecipients = message.CcRecipients,
        Date = message.Date,
        Snippet = message.Snippet,
        IsRead = message.IsRead,
        IsFlagged = message.IsFlagged,
        IsAnswered = message.IsAnswered,
        IsDraft = message.IsDraft,
        HasAttachments = message.HasAttachments,
        Size = message.Size,
        IsBodyDownloaded = message.IsBodyDownloaded,
        BodyTextPath = message.BodyTextPath,
        BodyHtmlPath = message.BodyHtmlPath,
    };

    private void ApplyHtmlRendering()
    {
        if (_rawHtml is null)
            return;

        var (sanitized, blockedAny) = HtmlSanitizer.Sanitize(_rawHtml, RemoteImagesAllowed);
        HtmlBody = sanitized;
        HasBlockedRemoteImages = blockedAny && !RemoteImagesAllowed;
    }

    private static async Task<(string? Text, string? Html)> ReadCachedBodyAsync(MailMessage message)
    {
        string? text = null;
        string? html = null;
        if (message.BodyTextPath is not null && File.Exists(message.BodyTextPath))
            text = await File.ReadAllTextAsync(message.BodyTextPath);
        if (message.BodyHtmlPath is not null && File.Exists(message.BodyHtmlPath))
            html = await File.ReadAllTextAsync(message.BodyHtmlPath);
        return (text, html);
    }

    private async Task<(string? Text, string? Html)> FetchAndCacheBodyAsync(MailMessage message)
    {
        var folder = await folderStore.GetByIdAsync(message.FolderId, CancellationToken.None)
            ?? throw new InvalidOperationException("フォルダが見つかりません。");
        if (folder.ImapFullName is null)
            throw new InvalidOperationException("このフォルダはローカル専用のため本文を取得できません。");

        var account = await accountStore.GetByIdAsync(message.AccountId, CancellationToken.None)
            ?? throw new InvalidOperationException("アカウントが見つかりません。");
        var password = credentialStore.GetImapPassword(account.Id)
            ?? throw new ImapAuthenticationException("保存されたパスワードが見つかりません。アカウントを再作成してください。");

        using var client = imapClientFactory();
        await client.ConnectAsync(account, password, CancellationToken.None);
        var (text, html) = await client.FetchBodyAsync(folder.ImapFullName, message.Uid, CancellationToken.None);
        await client.DisconnectAsync();

        Directory.CreateDirectory(appDataPaths.BodiesDirectory);

        string? textPath = null;
        string? htmlPath = null;
        if (text is not null)
        {
            textPath = Path.Combine(appDataPaths.BodiesDirectory, $"{message.Id}.txt");
            await File.WriteAllTextAsync(textPath, text);
        }
        if (html is not null)
        {
            htmlPath = Path.Combine(appDataPaths.BodiesDirectory, $"{message.Id}.html");
            await File.WriteAllTextAsync(htmlPath, html);
        }

        message.IsBodyDownloaded = true;
        message.BodyTextPath = textPath;
        message.BodyHtmlPath = htmlPath;
        await messageStore.SaveAsync(message, CancellationToken.None);

        return (text, html);
    }

    private async Task MarkAsReadAsync(MailMessage message)
    {
        if (message.IsRead)
            return;

        message.IsRead = true;
        await messageStore.SetReadAsync(message.Id, true, CancellationToken.None);

        var folder = await folderStore.GetByIdAsync(message.FolderId, CancellationToken.None);
        if (folder is not null && folder.UnreadCount > 0)
            await folderStore.UpdateCountsAsync(folder.Id, folder.UnreadCount - 1, folder.TotalCount, CancellationToken.None);

        await EnqueueOutboxActionAsync(
            message.AccountId, OutboxActionType.MarkRead, message.Id,
            targetFolderId: null, new OutboxMessageRef(message.FolderId, message.Uid));
    }
}
