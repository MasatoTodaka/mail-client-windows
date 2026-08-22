namespace MailClient.Core.Models;

// Queued mutating action, written in the same local transaction as the optimistic UI update.
// Replayed against the server by OutboxProcessor once connectivity is available.
public sealed class OutboxAction
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required OutboxActionType Type { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? TargetFolderId { get; set; }
    public string? PayloadJson { get; set; } // e.g. serialized MIME path for SendMessage
    public required DateTimeOffset CreatedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
