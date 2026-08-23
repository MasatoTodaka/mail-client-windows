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
    Task StartLiveUpdatesAsync(Guid accountId, CancellationToken ct);
    Task StopLiveUpdatesAsync(Guid accountId);

    event EventHandler<MessageArrivedEventArgs>? MessageArrived;
    event EventHandler<SyncProgressEventArgs>? SyncProgressChanged;
    event EventHandler<FolderCountsChangedEventArgs>? FolderCountsChanged;
}
