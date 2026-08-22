namespace MailClient.Core.Models;

// One row per (AccountId, FolderId); tracks incremental-sync watermarks.
public sealed class FolderSyncState
{
    public required Guid FolderId { get; init; }
    public uint UidValidity { get; set; }
    public uint LastSyncedUid { get; set; }
    public DateTimeOffset? OldestSyncedDate { get; set; }
    public bool InitialSyncComplete { get; set; }
}
