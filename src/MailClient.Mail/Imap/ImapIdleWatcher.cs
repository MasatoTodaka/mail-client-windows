using System.Net.NetworkInformation;
using MailClient.Core.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace MailClient.Mail.Imap;

// Owns one long-lived IMAP connection per account, dedicated to watching a folder (INBOX) for
// new messages: IMAP IDLE where the server supports it, otherwise periodic polling.
// Deliberately separate from IImapAccountClient's short-lived, per-operation connections so
// interactive fetch/move/delete never contends with this connection's IDLE state.
//
// The connection is expected to die regularly (PC sleep/resume, Wi-Fi switches, server or NAT
// idle timeouts), so the watcher reconnects on its own and raises FolderChanged after every
// reconnect to catch up on whatever arrived while it was offline.
public sealed class ImapIdleWatcher : IAsyncDisposable
{
    // Well under the 29-minute RFC 2177 limit, and short enough that home routers/NATs don't
    // silently drop the idle TCP session and that a dead connection is noticed within minutes.
    private static readonly TimeSpan IdleReissueInterval = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
    ];

    private readonly ImapClient _client = new();
    private Account? _account;
    private string? _password;
    private string? _folderFullName;
    private CancellationTokenSource? _stopCts;
    private CancellationTokenSource _networkChangedCts = new();
    private Task? _loopTask;

    // Raised (off the IDLE/poll loop's own thread) whenever the watched folder's message count
    // has changed, or after a reconnect. Callers are expected to re-sync the folder to find out
    // what actually arrived.
    public event EventHandler? FolderChanged;

    public async Task StartAsync(Account account, string password, string folderFullName, CancellationToken ct)
    {
        _account = account;
        _password = password;
        _folderFullName = folderFullName;

        var folder = await ConnectAndOpenAsync(ct);

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;

        _stopCts = new CancellationTokenSource();
        var loopToken = _stopCts.Token;
        _loopTask = Task.Run(() => RunAsync(folder, loopToken), CancellationToken.None);
    }

    private async Task<IMailFolder> ConnectAndOpenAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
        {
            try { await _client.DisconnectAsync(false, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* already dead */ }
        }

        var options = _account!.ImapSecurity switch
        {
            SecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SecurityMode.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None,
        };

        await _client.ConnectAsync(_account.ImapHost, _account.ImapPort, options, ct);
        await _client.AuthenticateAsync(_account.ImapUsername, _password!, ct);

        var folder = await _client.GetFolderAsync(_folderFullName!, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        return folder;
    }

    private async Task RunAsync(IMailFolder folder, CancellationToken stopToken)
    {
        void OnCountChanged(object? sender, EventArgs e) => FolderChanged?.Invoke(this, EventArgs.Empty);

        while (!stopToken.IsCancellationRequested)
        {
            // MailKit raises CountChanged synchronously as soon as it processes the server's
            // untagged EXISTS/EXPUNGE response — while IdleAsync is still blocked. Firing
            // FolderChanged from there (rather than after IdleAsync returns) is what gets new
            // mail reflected within seconds instead of only at the next IDLE reissue.
            folder.CountChanged += OnCountChanged;
            try
            {
                if (_client.Capabilities.HasFlag(ImapCapabilities.Idle))
                    await IdleLoopAsync(stopToken);
                else
                    await PollLoopAsync(stopToken);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // Connection lost (or the network changed underneath us) — reconnect below.
            }
            finally
            {
                folder.CountChanged -= OnCountChanged;
            }

            var reconnected = await ReconnectAsync(stopToken);
            if (reconnected is null)
                break;
            folder = reconnected;

            // Mail may have arrived while we were disconnected, and the fresh SELECT doesn't
            // raise CountChanged for it — have the caller re-sync now rather than wait for the
            // next arrival.
            FolderChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Returns null only when stopping.
    private async Task<IMailFolder?> ReconnectAsync(CancellationToken stopToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await Task.Delay(ReconnectDelays[Math.Min(attempt, ReconnectDelays.Length - 1)], stopToken);
                ResetNetworkChangedToken();
                return await ConnectAndOpenAsync(stopToken);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception)
            {
                // Still offline / server unreachable — keep backing off.
            }
        }
    }

    private async Task IdleLoopAsync(CancellationToken stopToken)
    {
        while (true)
        {
            using var doneCts = new CancellationTokenSource(IdleReissueInterval);
            using var doneLinked = CancellationTokenSource.CreateLinkedTokenSource(stopToken, doneCts.Token);
            using var abortLinked = CancellationTokenSource.CreateLinkedTokenSource(stopToken, _networkChangedCts.Token);
            // doneToken: stop idling normally on the reissue timer or a stop request.
            // cancellationToken: abort immediately on a stop request or a network change (the
            // socket is almost certainly dead then, and waiting for a read timeout takes minutes).
            await _client.IdleAsync(doneLinked.Token, abortLinked.Token);

            stopToken.ThrowIfCancellationRequested();
            if (!_client.IsConnected)
                throw new ServiceNotConnectedException();
        }
    }

    private async Task PollLoopAsync(CancellationToken stopToken)
    {
        while (true)
        {
            using var abortLinked = CancellationTokenSource.CreateLinkedTokenSource(stopToken, _networkChangedCts.Token);
            await Task.Delay(PollInterval, abortLinked.Token);

            // NOOP on the selected folder makes the server report any new EXISTS/EXPUNGE, which
            // MailKit surfaces as CountChanged. (STATUS on the selected mailbox is discouraged
            // by RFC 3501 and unreliable on some servers.) Throws on a dead connection, which
            // sends us through the reconnect path.
            await _client.NoOpAsync(abortLinked.Token);
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        try { _networkChangedCts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void ResetNetworkChangedToken()
    {
        if (!_networkChangedCts.IsCancellationRequested)
            return;
        var old = Interlocked.Exchange(ref _networkChangedCts, new CancellationTokenSource());
        old.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;

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
        _networkChangedCts.Dispose();
    }
}
