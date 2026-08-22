using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using Microsoft.Data.Sqlite;

namespace MailClient.Data.Repositories;

public sealed class AccountRepository(MailDbContext db) : IAccountStore
{
    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM accounts ORDER BY sort_order;";

        var accounts = new List<Account>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            accounts.Add(Map(reader));
        return accounts;
    }

    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM accounts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task SaveAsync(Account account, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts (
                id, display_name, email_address,
                imap_host, imap_port, imap_security, imap_username,
                smtp_host, smtp_port, smtp_security, smtp_username,
                is_enabled, sort_order)
            VALUES (
                $id, $displayName, $email,
                $imapHost, $imapPort, $imapSecurity, $imapUsername,
                $smtpHost, $smtpPort, $smtpSecurity, $smtpUsername,
                $isEnabled, $sortOrder)
            ON CONFLICT(id) DO UPDATE SET
                display_name = excluded.display_name,
                email_address = excluded.email_address,
                imap_host = excluded.imap_host,
                imap_port = excluded.imap_port,
                imap_security = excluded.imap_security,
                imap_username = excluded.imap_username,
                smtp_host = excluded.smtp_host,
                smtp_port = excluded.smtp_port,
                smtp_security = excluded.smtp_security,
                smtp_username = excluded.smtp_username,
                is_enabled = excluded.is_enabled,
                sort_order = excluded.sort_order;
            """;
        command.Parameters.AddWithValue("$id", account.Id.ToString());
        command.Parameters.AddWithValue("$displayName", account.DisplayName);
        command.Parameters.AddWithValue("$email", account.EmailAddress);
        command.Parameters.AddWithValue("$imapHost", account.ImapHost);
        command.Parameters.AddWithValue("$imapPort", account.ImapPort);
        command.Parameters.AddWithValue("$imapSecurity", (int)account.ImapSecurity);
        command.Parameters.AddWithValue("$imapUsername", account.ImapUsername);
        command.Parameters.AddWithValue("$smtpHost", account.SmtpHost);
        command.Parameters.AddWithValue("$smtpPort", account.SmtpPort);
        command.Parameters.AddWithValue("$smtpSecurity", (int)account.SmtpSecurity);
        command.Parameters.AddWithValue("$smtpUsername", account.SmtpUsername);
        command.Parameters.AddWithValue("$isEnabled", account.IsEnabled);
        command.Parameters.AddWithValue("$sortOrder", account.SortOrder);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM accounts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private static Account Map(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        DisplayName = reader.GetString(reader.GetOrdinal("display_name")),
        EmailAddress = reader.GetString(reader.GetOrdinal("email_address")),
        ImapHost = reader.GetString(reader.GetOrdinal("imap_host")),
        ImapPort = (int)reader.GetInt64(reader.GetOrdinal("imap_port")),
        ImapSecurity = (SecurityMode)reader.GetInt64(reader.GetOrdinal("imap_security")),
        ImapUsername = reader.GetString(reader.GetOrdinal("imap_username")),
        SmtpHost = reader.GetString(reader.GetOrdinal("smtp_host")),
        SmtpPort = (int)reader.GetInt64(reader.GetOrdinal("smtp_port")),
        SmtpSecurity = (SecurityMode)reader.GetInt64(reader.GetOrdinal("smtp_security")),
        SmtpUsername = reader.GetString(reader.GetOrdinal("smtp_username")),
        IsEnabled = reader.GetInt64(reader.GetOrdinal("is_enabled")) != 0,
        SortOrder = (int)reader.GetInt64(reader.GetOrdinal("sort_order")),
    };
}
