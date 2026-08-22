namespace MailClient.Core.Models;

// Passwords are never stored here — always round-trip through ICredentialStore.
public sealed class Account
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; set; }
    public required string EmailAddress { get; set; }

    public required string ImapHost { get; set; }
    public required int ImapPort { get; set; }
    public required SecurityMode ImapSecurity { get; set; }
    public required string ImapUsername { get; set; }

    public required string SmtpHost { get; set; }
    public required int SmtpPort { get; set; }
    public required SecurityMode SmtpSecurity { get; set; }
    public required string SmtpUsername { get; set; }

    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
}
