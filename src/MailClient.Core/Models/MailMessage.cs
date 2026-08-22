namespace MailClient.Core.Models;

public sealed class MailMessage
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid FolderId { get; init; }

    // IMAP UID within the folder; only meaningful together with the folder's UidValidity.
    public required uint Uid { get; set; }
    public string? MessageId { get; set; } // RFC Message-Id header, for dedupe/threading
    public string? InReplyTo { get; set; }
    public string? References { get; set; }

    public required string Subject { get; set; }
    public required string FromDisplay { get; set; }
    public required string FromAddress { get; set; }
    public required string ToRecipients { get; set; } // ';'-joined address list
    public string? CcRecipients { get; set; }

    public required DateTimeOffset Date { get; set; }
    public string Snippet { get; set; } = string.Empty;

    public bool IsRead { get; set; }
    public bool IsFlagged { get; set; }
    public bool IsAnswered { get; set; }
    public bool IsDraft { get; set; }
    public bool HasAttachments { get; set; }
    public int Size { get; set; }

    public bool IsBodyDownloaded { get; set; }
    public string? BodyTextPath { get; set; } // large bodies cached as files, not DB blobs
    public string? BodyHtmlPath { get; set; }
}
