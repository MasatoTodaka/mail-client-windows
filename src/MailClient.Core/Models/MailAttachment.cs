namespace MailClient.Core.Models;

public sealed class MailAttachment
{
    public required Guid Id { get; init; }
    public required Guid MessageId { get; init; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public int Size { get; set; }
    public string? LocalCachePath { get; set; } // null until the user opens/saves it
    public required string PartSpecifier { get; set; } // MIME part index, for on-demand fetch
}
