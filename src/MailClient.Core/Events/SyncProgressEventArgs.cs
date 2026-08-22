namespace MailClient.Core.Events;

public sealed class SyncProgressEventArgs(Guid accountId, Guid? folderId, string statusText, bool isComplete) : EventArgs
{
    public Guid AccountId { get; } = accountId;
    public Guid? FolderId { get; } = folderId;
    public string StatusText { get; } = statusText;
    public bool IsComplete { get; } = isComplete;
}
