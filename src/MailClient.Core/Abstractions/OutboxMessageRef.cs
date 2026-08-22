namespace MailClient.Core.Abstractions;

// JSON payload shape stored in OutboxAction.PayloadJson for MarkRead/MarkUnread/MarkFlagged/
// MarkUnflagged/Move/Delete actions: a snapshot of where the message lived on the server at
// enqueue time. Needed because the local optimistic update (e.g. moving a message to a new
// folder) happens immediately, before the action is replayed — by the time that happens, the
// message row's own FolderId already points at the new location.
public sealed record OutboxMessageRef(Guid FolderId, uint Uid);
