using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface IMailRuleStore
{
    Task<IReadOnlyList<MailRule>> GetByAccountAsync(Guid accountId, CancellationToken ct);
    Task SaveAsync(MailRule rule, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
