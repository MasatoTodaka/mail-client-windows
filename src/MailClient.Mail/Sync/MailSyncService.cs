using MailClient.Core.Abstractions;
using MailClient.Core.Events;
using MailClient.Core.Models;

namespace MailClient.Mail.Sync;

public sealed class MailSyncService(
    IAccountStore accountStore,
    IFolderStore folderStore,
    IMessageStore messageStore,
    ICredentialStore credentialStore,
    Func<IImapAccountClient> imapClientFactory) : IMailSyncService
{
    private const int RecentHeaderCount = 50;

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
        if (depth != SyncDepth.RecentOnly)
            throw new NotSupportedException($"SyncDepth.{depth} は未実装です（現状はRecentOnlyのみ対応）。");

        var folder = await folderStore.GetByIdAsync(folderId, ct)
            ?? throw new InvalidOperationException($"フォルダが見つかりません: {folderId}");
        if (folder.ImapFullName is null)
            return; // local-only folder (e.g. future Outbox) — nothing to sync from IMAP

        var account = await accountStore.GetByIdAsync(folder.AccountId, ct)
            ?? throw new InvalidOperationException($"アカウントが見つかりません: {folder.AccountId}");
        var password = credentialStore.GetImapPassword(account.Id)
            ?? throw new ImapAuthenticationException("保存されたパスワードが見つかりません。アカウントを再作成してください。");

        SyncProgressChanged?.Invoke(this, new SyncProgressEventArgs(account.Id, folder.Id, "同期中...", isComplete: false));

        try
        {
            using var client = imapClientFactory();
            await client.ConnectAsync(account, password, ct);
            var (uidValidity, uidNext) = await client.SelectFolderAsync(folder.ImapFullName, ct);
            var headers = await client.FetchHeadersAsync(folder.ImapFullName, fromUid: 1, toUid: null, maxCount: RecentHeaderCount, ct);
            await client.DisconnectAsync();

            var unreadCount = 0;
            foreach (var header in headers)
            {
                var existing = await messageStore.GetByUidAsync(folder.Id, header.Uid, ct);
                var toSave = Reconcile(header, folder.Id, existing);
                await messageStore.SaveAsync(toSave, ct);
                if (!toSave.IsRead)
                    unreadCount++;
            }

            // TotalCount here reflects locally-known messages after this partial sync, not the
            // server's true folder total — full-history tracking lands with SyncDepth.Full later.
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
                TotalCount = headers.Count,
                LastSyncedAt = DateTimeOffset.UtcNow,
            };
            await folderStore.SaveAsync(updatedFolder, ct);

            FolderCountsChanged?.Invoke(this, new FolderCountsChangedEventArgs(folder.Id, unreadCount, headers.Count));
            SyncProgressChanged?.Invoke(this, new SyncProgressEventArgs(account.Id, folder.Id, "同期完了", isComplete: true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SyncProgressChanged?.Invoke(this, new SyncProgressEventArgs(account.Id, folder.Id, $"同期エラー: {ex.Message}", isComplete: true));
            throw;
        }
    }

    public Task StartLiveUpdatesAsync(Guid accountId, CancellationToken ct) =>
        throw new NotSupportedException("ライブ更新(IMAP IDLE)はM8で実装予定です。");

    public Task StopLiveUpdatesAsync(Guid accountId) =>
        throw new NotSupportedException("ライブ更新(IMAP IDLE)はM8で実装予定です。");

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
