using MailClient.Core.Abstractions;
using Windows.Security.Credentials;

namespace MailClient.Platform;

// Passwords round-trip exclusively through Windows Credential Locker (PasswordVault) — never
// through SQLite or any file. PasswordVault requires package identity, which is why the app is
// packaged as MSIX (see MailClient.App.csproj) rather than run as a plain unpackaged exe.
public sealed class CredentialLockerStore : ICredentialStore
{
    private const string ImapResource = "MailClient.Imap";
    private const string SmtpResource = "MailClient.Smtp";

    public void SaveImapPassword(Guid accountId, string password) => Save(ImapResource, accountId, password);

    public void SaveSmtpPassword(Guid accountId, string password) => Save(SmtpResource, accountId, password);

    public string? GetImapPassword(Guid accountId) => TryRetrieve(ImapResource, accountId);

    public string? GetSmtpPassword(Guid accountId) => TryRetrieve(SmtpResource, accountId);

    public void Remove(Guid accountId)
    {
        RemoveIfExists(ImapResource, accountId);
        RemoveIfExists(SmtpResource, accountId);
    }

    private static void Save(string resource, Guid accountId, string password)
    {
        RemoveIfExists(resource, accountId);
        new PasswordVault().Add(new PasswordCredential(resource, accountId.ToString(), password));
    }

    private static string? TryRetrieve(string resource, Guid accountId)
    {
        try
        {
            var credential = new PasswordVault().Retrieve(resource, accountId.ToString());
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception)
        {
            // PasswordVault has no TryGet API — it throws when the (resource, user) pair is absent.
            return null;
        }
    }

    private static void RemoveIfExists(string resource, Guid accountId)
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(resource, accountId.ToString()));
        }
        catch (Exception)
        {
            // Nothing stored yet — nothing to remove.
        }
    }
}
