using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface IMessageStore
{
    Task<IReadOnlyList<MailMessage>> GetPageAsync(Guid folderId, int skip, int take, CancellationToken ct);
    Task<MailMessage?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<MailMessage?> GetByUidAsync(Guid folderId, uint uid, CancellationToken ct);
    Task<uint?> GetMinUidAsync(Guid folderId, CancellationToken ct);
    Task<(int Total, int Unread)> GetFolderCountsAsync(Guid folderId, CancellationToken ct);

    // One-time local cleanup: re-runs SubjectCharsetFixer over every cached subject and rewrites
    // any that change. Needed because the fixer only runs on freshly-fetched headers going
    // forward — this catches subjects that were cached before the fixer existed. Returns the
    // number of rows updated.
    Task<int> FixMojibakeSubjectsAsync(CancellationToken ct);
    Task SaveAsync(MailMessage message, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task SetReadAsync(Guid id, bool isRead, CancellationToken ct);
    Task SetFlaggedAsync(Guid id, bool isFlagged, CancellationToken ct);
}
