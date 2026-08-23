using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// M5: on-demand body fetch (cached to a local file, never re-downloaded once cached), HTML
// rendered with remote images blocked by default, and read-state marking on open.
// M6: flag/delete(→Trash)/archive delegate to MessageActionService (shared with the message
// list's right-click menu), which acts immediately on local state then queues the matching
// OutboxAction so the change survives being offline and replays once connectivity returns.
public sealed partial class ReadingPaneViewModel(
    IMessageStore messageStore,
    IFolderStore folderStore,
    IAccountStore accountStore,
    ICredentialStore credentialStore,
    Func<IImapAccountClient> imapClientFactory,
    AppDataPaths appDataPaths,
    MessageActionService messageActions) : ViewModelBase
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

        await messageActions.ToggleFlagAsync(message, CancellationToken.None);
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
        var error = await messageActions.MoveToSpecialFolderAsync(message, targetUse, CancellationToken.None);
        if (error is not null)
        {
            ErrorMessage = error;
            return;
        }

        SelectedMessage = null;
        PlainTextBody = null;
        HtmlBody = null;
        IsHtml = false;
        MessageStateChanged?.Invoke(this, EventArgs.Empty);
    }

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

    private Task MarkAsReadAsync(MailMessage message) => messageActions.SetReadAsync(message, true, CancellationToken.None);
}
