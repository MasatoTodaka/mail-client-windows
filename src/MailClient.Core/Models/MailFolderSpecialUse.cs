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
    Flagged, // local-only virtual folder: aggregates flagged messages from every real folder
    Today, // local-only virtual folder: aggregates messages received since local midnight
}
