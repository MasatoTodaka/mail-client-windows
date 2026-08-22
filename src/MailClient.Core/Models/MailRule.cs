namespace MailClient.Core.Models;

// A sender/subject filter that files matching mail into a folder automatically. At least one of
// SenderContains/SubjectContains must be set — an all-null rule would match everything.
public sealed class MailRule
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public string? SenderContains { get; set; }
    public string? SubjectContains { get; set; }
    public required Guid TargetFolderId { get; set; }
    public int SortOrder { get; set; }
}
