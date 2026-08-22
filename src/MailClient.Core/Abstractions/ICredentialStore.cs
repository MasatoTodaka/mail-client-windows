namespace MailClient.Core.Abstractions;

// Default implementation (MailClient.Platform.CredentialLockerStore) uses Windows Credential Locker.
// Passwords must never be persisted anywhere else (no SQLite columns, no config files).
public interface ICredentialStore
{
    void SaveImapPassword(Guid accountId, string password);
    void SaveSmtpPassword(Guid accountId, string password);
    string? GetImapPassword(Guid accountId);
    string? GetSmtpPassword(Guid accountId);
    void Remove(Guid accountId);
}
