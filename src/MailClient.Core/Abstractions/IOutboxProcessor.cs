namespace MailClient.Core.Abstractions;

// Replays queued OutboxAction rows for an account against the server (implemented over MailKit
// in MailClient.Mail). Safe to call opportunistically — a no-op if nothing is pending, and a
// no-op (leaving everything queued) if the account can't currently be reached.
public interface IOutboxProcessor
{
    Task ProcessAsync(Guid accountId, CancellationToken ct);
}
