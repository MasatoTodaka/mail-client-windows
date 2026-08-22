namespace MailClient.Core.Models;

public enum MailFolderSpecialUse
{
    None,
    Inbox,
    Sent,
    Drafts,
    Trash,
    Junk,
    Archive,
    Outbox, // local-only pseudo-folder for queued outgoing mail
}
