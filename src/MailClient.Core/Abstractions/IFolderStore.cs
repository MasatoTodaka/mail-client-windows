using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface IFolderStore
{
    Task<IReadOnlyList<MailFolder>> GetByAccountAsync(Guid accountId, CancellationToken ct);
    Task<MailFolder?> GetByIdAsync(Guid id, CancellationToken ct);
    Task SaveAsync(MailFolder folder, CancellationToken ct);
    Task UpdateCountsAsync(Guid folderId, int unreadCount, int totalCount, CancellationToken ct);
}
