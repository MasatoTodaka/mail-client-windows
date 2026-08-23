using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface IMessageStore
{
    Task<IReadOnlyList<MailMessage>> GetPageAsync(Guid folderId, int skip, int take, CancellationToken ct);
    Task<MailMessage?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<MailMessage?> GetByUidAsync(Guid folderId, uint uid, CancellationToken ct);
    Task<uint?> GetMinUidAsync(Guid folderId, CancellationToken ct);
    Task<(int Total, int Unread)> GetFolderCountsAsync(Guid folderId, CancellationToken ct);

    // Flagged messages across every real folder in the account — backs the virtual "フラグ付き"
    // smart folder (aggregated view, not a real IMAP folder).
    Task<IReadOnlyList<MailMessage>> GetFlaggedPageAsync(Guid accountId, int skip, int take, CancellationToken ct);
    Task<int> GetFlaggedCountAsync(Guid accountId, CancellationToken ct);

    // Messages received since local midnight, across every real folder in the account — backs
    // the virtual "今日" smart folder.
    Task<IReadOnlyList<MailMessage>> GetTodayPageAsync(Guid accountId, int skip, int take, CancellationToken ct);
    Task<int> GetTodayCountAsync(Guid accountId, CancellationToken ct);

    // Every locally-known message across all of an account's folders — used to apply mail rules
    // retroactively ("run now"), not for normal list display (which pages per folder).
    Task<IReadOnlyList<MailMessage>> GetByAccountAsync(Guid accountId, CancellationToken ct);

    // One-time local cleanup: re-runs MojibakeFixer over every cached subject and rewrites
    // any that change. Needed because the fixer only runs on freshly-fetched headers going
    // forward — this catches subjects that were cached before the fixer existed. Returns the
    // number of rows updated.
    Task<int> FixMojibakeSubjectsAsync(CancellationToken ct);

    // Messages with a cached body file on disk (IsBodyDownloaded), for the equivalent body-side
    // backfill — that one has to happen at the file-I/O layer (MailClient.App), not here.
    Task<IReadOnlyList<MailMessage>> GetDownloadedBodyMessagesAsync(CancellationToken ct);
    Task SaveAsync(MailMessage message, CancellationToken ct);
    Task UpdateDateAsync(Guid messageId, DateTimeOffset date, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task SetReadAsync(Guid id, bool isRead, CancellationToken ct);
    Task SetFlaggedAsync(Guid id, bool isFlagged, CancellationToken ct);
}
