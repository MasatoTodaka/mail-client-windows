using MailClient.Core.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace MailClient.Mail.Imap;

// Owns one long-lived IMAP connection per account, dedicated to watching a folder (INBOX) for
// new messages: IMAP IDLE where the server supports it, otherwise periodic re-SELECT polling.
// Deliberately separate from IImapAccountClient's short-lived, per-operation connections so
// interactive fetch/move/delete never contends with this connection's IDLE state.
public sealed class ImapIdleWatcher : IAsyncDisposable
{
    private static readonly TimeSpan IdleReissueInterval = TimeSpan.FromMinutes(9);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly ImapClient _client = new();
    private CancellationTokenSource? _stopCts;
    private Task? _loopTask;

    // Raised (off the IDLE/poll loop's own thread) whenever the watched folder's message count
    // has grown. Callers are expected to re-sync the folder to find out what actually arrived.
    public event EventHandler? FolderChanged;

    public async Task StartAsync(Account account, string password, string folderFullName, CancellationToken ct)
    {
        var options = account.ImapSecurity switch
        {
            SecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SecurityMode.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None,
        };

        await _client.ConnectAsync(account.ImapHost, account.ImapPort, options, ct);
        await _client.AuthenticateAsync(account.ImapUsername, password, ct);

        var folder = await _client.GetFolderAsync(folderFullName, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        _stopCts = new CancellationTokenSource();
        var loopToken = _stopCts.Token;
        _loopTask = _client.Capabilities.HasFlag(ImapCapabilities.Idle)
            ? Task.Run(() => IdleLoopAsync(folder, loopToken), CancellationToken.None)
            : Task.Run(() => PollLoopAsync(folder, loopToken), CancellationToken.None);
    }

    private async Task IdleLoopAsync(IMailFolder folder, CancellationToken stopToken)
    {
        // MailKit raises CountChanged synchronously as soon as it processes the server's
        // untagged EXISTS response — while IdleAsync is still blocked waiting for the done/
        // cancellation token. Firing FolderChanged from *here* (rather than after IdleAsync
        // returns) is what gets new mail reflected within seconds instead of only at the next
        // ~9-minute IDLE reissue.
        void OnCountChanged(object? sender, EventArgs e) => FolderChanged?.Invoke(this, EventArgs.Empty);
        folder.CountChanged += OnCountChanged;

        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                try
                {
                    using var doneCts = new CancellationTokenSource(IdleReissueInterval);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stopToken, doneCts.Token);
                    // doneToken (linkedCts): stop idling normally on our 9-minute timer or a stop
                    // request. cancellationToken (stopToken): abort immediately on a stop request.
                    await _client.IdleAsync(linkedCts.Token, stopToken);
                }
                catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception)
                {
                    // Connection hiccup — back off briefly and let the loop retry.
                    try { await Task.Delay(TimeSpan.FromSeconds(30), stopToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            folder.CountChanged -= OnCountChanged;
        }
    }

    private async Task PollLoopAsync(IMailFolder folder, CancellationToken stopToken)
    {
        var lastCount = folder.Count;
        while (!stopToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stopToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await folder.StatusAsync(StatusItems.Count, stopToken);
                if (folder.Count != lastCount)
                {
                    lastCount = folder.Count;
                    FolderChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception)
            {
                // Transient error — try again next tick.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopCts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask; }
            catch { /* the loop already handles/swallows its own exceptions */ }
        }

        if (_client.IsConnected)
        {
            try { await _client.DisconnectAsync(true); }
            catch { /* best-effort */ }
        }

        _client.Dispose();
        _stopCts?.Dispose();
    }
}
