using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface IFolderStore
{
    Task<IReadOnlyList<MailFolder>> GetByAccountAsync(Guid accountId, CancellationToken ct);
    Task<MailFolder?> GetByIdAsync(Guid id, CancellationToken ct);
    Task SaveAsync(MailFolder folder, CancellationToken ct);
    Task UpdateCountsAsync(Guid folderId, int unreadCount, int totalCount, CancellationToken ct);

    // Persists a new display order for a set of sibling folders (e.g. after a sidebar drag
    // reorder) — orderedFolderIds[i]'s sort_order becomes i.
    Task ReorderAsync(IReadOnlyList<Guid> orderedFolderIds, CancellationToken ct);
}
