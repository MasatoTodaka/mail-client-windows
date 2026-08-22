using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface IMessageStore
{
    Task<IReadOnlyList<MailMessage>> GetPageAsync(Guid folderId, int skip, int take, CancellationToken ct);
    Task<MailMessage?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<MailMessage?> GetByUidAsync(Guid folderId, uint uid, CancellationToken ct);
    Task<uint?> GetMinUidAsync(Guid folderId, CancellationToken ct);
    Task<(int Total, int Unread)> GetFolderCountsAsync(Guid folderId, CancellationToken ct);
    Task SaveAsync(MailMessage message, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task SetReadAsync(Guid id, bool isRead, CancellationToken ct);
    Task SetFlaggedAsync(Guid id, bool isFlagged, CancellationToken ct);
}
