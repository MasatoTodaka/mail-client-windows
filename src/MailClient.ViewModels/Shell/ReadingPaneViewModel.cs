using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// M5: on-demand body fetch (cached to a local file, never re-downloaded once cached), HTML
// rendered with remote images blocked by default, and read-state marking on open.
public sealed partial class ReadingPaneViewModel(
    IMessageStore messageStore,
    IFolderStore folderStore,
    IAccountStore accountStore,
    ICredentialStore credentialStore,
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
            MessageOpened?.Invoke(this, EventArgs.Empty);
        }
    }

    // Raised after every load attempt so the message list can refresh its (possibly now-read) row.
    public event EventHandler? MessageOpened;

    [RelayCommand]
    private void LoadRemoteImages()
    {
        RemoteImagesAllowed = true;
        ApplyHtmlRendering();
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

    private async Task MarkAsReadAsync(MailMessage message)
    {
        if (message.IsRead)
            return;

        message.IsRead = true;
        await messageStore.SetReadAsync(message.Id, true, CancellationToken.None);

        var folder = await folderStore.GetByIdAsync(message.FolderId, CancellationToken.None);
        if (folder is not null && folder.UnreadCount > 0)
            await folderStore.UpdateCountsAsync(folder.Id, folder.UnreadCount - 1, folder.TotalCount, CancellationToken.None);
    }
}
