using System.Collections.Concurrent;
using MailClient.Core.Abstractions;
using MailClient.Core.Events;
using MailClient.Core.Models;
using MailClient.Mail.Imap;

namespace MailClient.Mail.Sync;

public sealed class MailSyncService(
    IAccountStore accountStore,
    IFolderStore folderStore,
    IMessageStore messageStore,
    ICredentialStore credentialStore,
    Func<IImapAccountClient> imapClientFactory) : IMailSyncService
{
    private const int RecentHeaderCount = 50;

    private readonly ConcurrentDictionary<Guid, ImapIdleWatcher> _watchers = new();

    public event EventHandler<MessageArrivedEventArgs>? MessageArrived;
    public event EventHandler<SyncProgressEventArgs>? SyncProgressChanged;
    public event EventHandler<FolderCountsChangedEventArgs>? FolderCountsChanged;

    public async Task InitialSyncAsync(Guid accountId, CancellationToken ct)
    {
        var folders = await folderStore.GetByAccountAsync(accountId, ct);
        var inbox = folders.FirstOrDefault(f => f.SpecialUse == MailFolderSpecialUse.Inbox)
            ?? folders.FirstOrDefault(f => string.Equals(f.ImapFullName, "INBOX", StringComparison.OrdinalIgnoreCase));

        if (inbox is not null)
            await SyncFolderAsync(inbox.Id, SyncDepth.RecentOnly, ct);
    }

    public async Task SyncFolderAsync(Guid folderId, SyncDepth depth, CancellationToken ct)
    {
        if (depth == SyncDepth.Full)
            throw new NotSupportedException("SyncDepth.Full は未実装です。ExtendBackward で少しずつ過去のメールを読み込んでください。");

        var folder = await folderStore.GetByIdAsync(folderId, ct)
            ?? throw new InvalidOperationException($"フォルダが見つかりません: {folderId}");
        if (folder.ImapFullName is null)
            return; // local-only folder (e.g. future Outbox) — nothing to sync from IMAP

        var account = await accountStore.GetByIdAsync(folder.AccountId, ct)
            ?? throw new InvalidOperationException($"アカウントが見つかりません: {folder.AccountId}");
        var password = credentialStore.GetImapPassword(account.Id)
            ?? throw new ImapAuthenticationException("保存されたパスワードが見つかりません。アカウントを再作成してください。");

        uint fromUid = 1;
        uint? toUid = null;

        if (depth == SyncDepth.ExtendBackward)
        {
            var minUid = await messageStore.GetMinUidAsync(folder.Id, ct);
            if (minUid is null or <= 1)
            {
                // Nothing local yet, or already at the oldest message — nothing further to fetch.
                SyncProgressChanged?.Invoke(this, new SyncProgressEventArgs(account.Id, folder.Id, "これ以上過去のメールはありません", isComplete: true));
                return;
            }
            toUid = minUid.Value - 1;
        }

        SyncProgressChanged?.Invoke(this, new SyncProgressEventArgs(account.Id, folder.Id, "同期中...", isComplete: false));

        try
        {
            using var client = imapClientFactory();
            await client.ConnectAsync(account, password, ct);
            var (uidValidity, uidNext) = await client.SelectFolderAsync(folder.ImapFullName, ct);
            var headers = await client.FetchHeadersAsync(folder.ImapFullName, fromUid, toUid, maxCount: RecentHeaderCount, ct);
            await client.DisconnectAsync();

            foreach (var header in headers)
            {
                var existing = await messageStore.GetByUidAsync(folder.Id, header.Uid, ct);
                var toSave = Reconcile(header, folder.Id, existing);
                await messageStore.SaveAsync(toSave, ct);

                // Only RecentOnly syncs represent genuinely new mail; ExtendBackward is the user
                // deliberately paging into history and must not trigger arrival notifications.
                if (existing is null && depth == SyncDepth.RecentOnly)
                    MessageArrived?.Invoke(this, new MessageArrivedEventArgs(toSave));
            }

            // Reflects all locally-known messages for the folder (grows as ExtendBackward pages
            // further back), not the server's true folder total — full-history tracking would
            // need SyncDepth.Full.
            var (totalCount, unreadCount) = await messageStore.GetFolderCountsAsync(folder.Id, ct);

            var updatedFolder = new MailFolder
            {
                Id = folder.Id,
                AccountId = folder.AccountId,
                ImapFullName = folder.ImapFullName,
                DisplayName = folder.DisplayName,
                SpecialUse = folder.SpecialUse,
                ParentFolderId = folder.ParentFolderId,
                UidValidity = uidValidity,
                UidNext = uidNext,
                HighestModSeq = folder.HighestModSeq,
                UnreadCount = unreadCount,
                TotalCount = totalCount,
                LastSyncedAt = DateTimeOffset.UtcNow,
            };
            await folderStore.SaveAsync(updatedFolder, ct);

            FolderCountsChanged?.Invoke(this, new FolderCountsChangedEventArgs(folder.Id, unreadCount, totalCount));
            SyncProgressChanged?.Invoke(this, new SyncProgressEventArgs(account.Id, folder.Id, "同期完了", isComplete: true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SyncProgressChanged?.Invoke(this, new SyncProgressEventArgs(account.Id, folder.Id, $"同期エラー: {ex.Message}", isComplete: true));
            throw;
        }
    }

    public async Task StartLiveUpdatesAsync(Guid accountId, CancellationToken ct)
    {
        if (_watchers.ContainsKey(accountId))
            return; // already watching

        var account = await accountStore.GetByIdAsync(accountId, ct);
        var password = account is null ? null : credentialStore.GetImapPassword(accountId);
        if (account is null || password is null)
            return;

        var folders = await folderStore.GetByAccountAsync(accountId, ct);
        var inbox = folders.FirstOrDefault(f => f.SpecialUse == MailFolderSpecialUse.Inbox)
            ?? folders.FirstOrDefault(f => string.Equals(f.ImapFullName, "INBOX", StringComparison.OrdinalIgnoreCase));
        if (inbox?.ImapFullName is null)
            return;

        var watcher = new ImapIdleWatcher();
        watcher.FolderChanged += async (_, _) =>
        {
            try
            {
                await SyncFolderAsync(inbox.Id, SyncDepth.RecentOnly, CancellationToken.None);
            }
            catch
            {
                // SyncFolderAsync already reports this via SyncProgressChanged; the watcher loop
                // itself must keep running regardless.
            }
        };

        if (!_watchers.TryAdd(accountId, watcher))
        {
            await watcher.DisposeAsync();
            return;
        }

        try
        {
            await watcher.StartAsync(account, password, inbox.ImapFullName, ct);
        }
        catch
        {
            _watchers.TryRemove(accountId, out _);
            await watcher.DisposeAsync();
            throw;
        }
    }

    public async Task StopLiveUpdatesAsync(Guid accountId)
    {
        if (_watchers.TryRemove(accountId, out var watcher))
            await watcher.DisposeAsync();
    }

    // Keeps the message's local identity and any already-downloaded body across re-syncs of
    // the same (folder, uid) — re-fetching headers must not clobber M5's downloaded body state.
    private static MailMessage Reconcile(MailMessage header, Guid folderId, MailMessage? existing) => new()
    {
        Id = existing?.Id ?? header.Id,
        AccountId = header.AccountId,
        FolderId = folderId,
        Uid = header.Uid,
        MessageId = header.MessageId,
        InReplyTo = header.InReplyTo,
        References = header.References,
        Subject = header.Subject,
        FromDisplay = header.FromDisplay,
        FromAddress = header.FromAddress,
        ToRecipients = header.ToRecipients,
        CcRecipients = header.CcRecipients,
        Date = header.Date,
        Snippet = header.Snippet,
        IsRead = header.IsRead,
        IsFlagged = header.IsFlagged,
        IsAnswered = header.IsAnswered,
        IsDraft = header.IsDraft,
        HasAttachments = header.HasAttachments,
        Size = header.Size,
        IsBodyDownloaded = existing?.IsBodyDownloaded ?? false,
        BodyTextPath = existing?.BodyTextPath,
        BodyHtmlPath = existing?.BodyHtmlPath,
    };
}
