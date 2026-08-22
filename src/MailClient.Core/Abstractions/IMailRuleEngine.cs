using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface IMailRuleEngine
{
    // Applies the account's first matching rule to the message (queues a move), if any.
    // Returns true if a rule matched.
    Task<bool> TryApplyAsync(MailMessage message, CancellationToken ct);

    // Runs TryApplyAsync over every locally-known message for the account (e.g. after the user
    // adds a new rule and wants it applied retroactively). Returns how many messages moved.
    Task<int> RunNowAsync(Guid accountId, CancellationToken ct);
}
