namespace MailClient.Core.Events;

public sealed class FolderCountsChangedEventArgs(Guid folderId, int unreadCount, int totalCount) : EventArgs
{
    public Guid FolderId { get; } = folderId;
    public int UnreadCount { get; } = unreadCount;
    public int TotalCount { get; } = totalCount;
}
