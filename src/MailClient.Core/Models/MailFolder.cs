namespace MailClient.Core.Models;

public sealed class MailFolder
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }

    // Server-side path, e.g. "INBOX.Archive". Null for local-only folders (Outbox).
    public string? ImapFullName { get; set; }
    public required string DisplayName { get; set; }
    public MailFolderSpecialUse SpecialUse { get; set; } = MailFolderSpecialUse.None;
    public Guid? ParentFolderId { get; set; }

    // UIDVALIDITY changing invalidates the entire local cache for this folder (IMAP spec).
    public uint UidValidity { get; set; }
    public uint UidNext { get; set; }
    public ulong? HighestModSeq { get; set; }

    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}
