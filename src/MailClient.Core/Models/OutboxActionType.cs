namespace MailClient.Core.Models;

public enum OutboxActionType
{
    MarkRead,
    MarkUnread,
    MarkFlagged,
    MarkUnflagged,
    Move,
    Delete,
    SendMessage,
    Append,
}
