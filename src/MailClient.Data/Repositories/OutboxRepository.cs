using System.Globalization;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using Microsoft.Data.Sqlite;

namespace MailClient.Data.Repositories;

// A "succeeded" action is simply removed — its effect already landed in the messages/folders
// tables, so nothing about it needs to stay queued. A "failed" one stays, with attempt_count
// and last_error updated, so OutboxProcessor can retry it on the next connectivity window.
public sealed class OutboxRepository(MailDbContext db) : IOutboxStore
{
    public async Task EnqueueAsync(OutboxAction action, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO outbox_actions (
                id, account_id, type, message_id, target_folder_id, payload_json,
                created_at, attempt_count, last_error)
            VALUES (
                $id, $accountId, $type, $messageId, $targetFolderId, $payloadJson,
                $createdAt, $attemptCount, $lastError);
            """;
        command.Parameters.AddWithValue("$id", action.Id.ToString());
        command.Parameters.AddWithValue("$accountId", action.AccountId.ToString());
        command.Parameters.AddWithValue("$type", (int)action.Type);
        command.Parameters.AddWithValue("$messageId", (object?)action.MessageId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$targetFolderId", (object?)action.TargetFolderId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$payloadJson", (object?)action.PayloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", action.CreatedAt.ToString("o"));
        command.Parameters.AddWithValue("$attemptCount", action.AttemptCount);
        command.Parameters.AddWithValue("$lastError", (object?)action.LastError ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxAction>> GetPendingAsync(Guid accountId, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM outbox_actions WHERE account_id = $accountId ORDER BY created_at;";
        command.Parameters.AddWithValue("$accountId", accountId.ToString());

        var actions = new List<OutboxAction>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            actions.Add(Map(reader));
        return actions;
    }

    public async Task MarkSucceededAsync(Guid actionId, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM outbox_actions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", actionId.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(Guid actionId, string error, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE outbox_actions
            SET attempt_count = attempt_count + 1, last_error = $error
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$id", actionId.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private static OutboxAction Map(SqliteDataReader reader)
    {
        string? GetNullableString(string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        var messageIdText = GetNullableString("message_id");
        var targetFolderIdText = GetNullableString("target_folder_id");

        return new OutboxAction
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            AccountId = Guid.Parse(reader.GetString(reader.GetOrdinal("account_id"))),
            Type = (OutboxActionType)reader.GetInt64(reader.GetOrdinal("type")),
            MessageId = messageIdText is null ? null : Guid.Parse(messageIdText),
            TargetFolderId = targetFolderIdText is null ? null : Guid.Parse(targetFolderIdText),
            PayloadJson = GetNullableString("payload_json"),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            AttemptCount = (int)reader.GetInt64(reader.GetOrdinal("attempt_count")),
            LastError = GetNullableString("last_error"),
        };
    }
}
