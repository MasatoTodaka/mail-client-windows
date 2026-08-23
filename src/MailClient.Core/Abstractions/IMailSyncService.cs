using MailClient.Core.Events;
using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public enum SyncDepth
{
    RecentOnly,    // e.g. most recent ~50 headers
    ExtendBackward, // next page further into history
    Full,
}

public interface IMailSyncService
{
    Task InitialSyncAsync(Guid accountId, CancellationToken ct);
    Task SyncFolderAsync(Guid folderId, SyncDepth depth, CancellationToken ct);

    // Lightweight (IMAP STATUS, no header fetch) unread/total badge refresh for every folder in
    // the account, including ones the user hasn't opened yet.
    Task SyncAllFolderCountsAsync(Guid accountId, CancellationToken ct);

    // One-time-per-account: re-fetches IMAP INTERNALDATE for every already-cached message so
    // its sort date reflects actual server arrival, not the sender's Date: header. No-ops once
    // already run for this account (tracked via ISettingsStore).
    Task BackfillReceivedDatesAsync(Guid accountId, CancellationToken ct);
    Task StartLiveUpdatesAsync(Guid accountId, CancellationToken ct);
    Task StopLiveUpdatesAsync(Guid accountId);

    event EventHandler<MessageArrivedEventArgs>? MessageArrived;
    event EventHandler<SyncProgressEventArgs>? SyncProgressChanged;
    event EventHandler<FolderCountsChangedEventArgs>? FolderCountsChanged;
}
