using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using Microsoft.Data.Sqlite;

namespace MailClient.Data.Repositories;

public sealed class MailRuleRepository(MailDbContext db) : IMailRuleStore
{
    public async Task<IReadOnlyList<MailRule>> GetByAccountAsync(Guid accountId, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mail_rules WHERE account_id = $accountId ORDER BY sort_order;";
        command.Parameters.AddWithValue("$accountId", accountId.ToString());

        var rules = new List<MailRule>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rules.Add(Map(reader));
        return rules;
    }

    public async Task SaveAsync(MailRule rule, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mail_rules (id, account_id, sender_contains, subject_contains, target_folder_id, sort_order)
            VALUES ($id, $accountId, $senderContains, $subjectContains, $targetFolderId, $sortOrder)
            ON CONFLICT(id) DO UPDATE SET
                sender_contains = excluded.sender_contains,
                subject_contains = excluded.subject_contains,
                target_folder_id = excluded.target_folder_id,
                sort_order = excluded.sort_order;
            """;
        command.Parameters.AddWithValue("$id", rule.Id.ToString());
        command.Parameters.AddWithValue("$accountId", rule.AccountId.ToString());
        command.Parameters.AddWithValue("$senderContains", (object?)rule.SenderContains ?? DBNull.Value);
        command.Parameters.AddWithValue("$subjectContains", (object?)rule.SubjectContains ?? DBNull.Value);
        command.Parameters.AddWithValue("$targetFolderId", rule.TargetFolderId.ToString());
        command.Parameters.AddWithValue("$sortOrder", rule.SortOrder);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM mail_rules WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private static MailRule Map(SqliteDataReader reader)
    {
        var senderOrdinal = reader.GetOrdinal("sender_contains");
        var subjectOrdinal = reader.GetOrdinal("subject_contains");
        return new MailRule
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            AccountId = Guid.Parse(reader.GetString(reader.GetOrdinal("account_id"))),
            SenderContains = reader.IsDBNull(senderOrdinal) ? null : reader.GetString(senderOrdinal),
            SubjectContains = reader.IsDBNull(subjectOrdinal) ? null : reader.GetString(subjectOrdinal),
            TargetFolderId = Guid.Parse(reader.GetString(reader.GetOrdinal("target_folder_id"))),
            SortOrder = (int)reader.GetInt64(reader.GetOrdinal("sort_order")),
        };
    }
}
