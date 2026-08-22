using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface IOutboxStore
{
    Task EnqueueAsync(OutboxAction action, CancellationToken ct);
    Task<IReadOnlyList<OutboxAction>> GetPendingAsync(Guid accountId, CancellationToken ct);
    Task MarkSucceededAsync(Guid actionId, CancellationToken ct);
    Task MarkFailedAsync(Guid actionId, string error, CancellationToken ct);
}
