using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface IAccountStore
{
    Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct);
    Task<Account?> GetByIdAsync(Guid id, CancellationToken ct);
    Task SaveAsync(Account account, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
