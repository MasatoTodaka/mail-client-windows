using System.Globalization;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using Microsoft.Data.Sqlite;

namespace MailClient.Data.Repositories;

public sealed class FolderRepository(MailDbContext db) : IFolderStore
{
    public async Task<IReadOnlyList<MailFolder>> GetByAccountAsync(Guid accountId, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM folders WHERE account_id = $accountId ORDER BY display_name;";
        command.Parameters.AddWithValue("$accountId", accountId.ToString());

        var folders = new List<MailFolder>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            folders.Add(Map(reader));
        return folders;
    }

    public async Task<MailFolder?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM folders WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task SaveAsync(MailFolder folder, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO folders (
                id, account_id, imap_full_name, display_name, special_use, parent_folder_id,
                uid_validity, uid_next, highest_mod_seq, unread_count, total_count, last_synced_at)
            VALUES (
                $id, $accountId, $imapFullName, $displayName, $specialUse, $parentFolderId,
                $uidValidity, $uidNext, $highestModSeq, $unreadCount, $totalCount, $lastSyncedAt)
            ON CONFLICT(id) DO UPDATE SET
                account_id = excluded.account_id,
                imap_full_name = excluded.imap_full_name,
                display_name = excluded.display_name,
                special_use = excluded.special_use,
                parent_folder_id = excluded.parent_folder_id,
                uid_validity = excluded.uid_validity,
                uid_next = excluded.uid_next,
                highest_mod_seq = excluded.highest_mod_seq,
                unread_count = excluded.unread_count,
                total_count = excluded.total_count,
                last_synced_at = excluded.last_synced_at;
            """;
        command.Parameters.AddWithValue("$id", folder.Id.ToString());
        command.Parameters.AddWithValue("$accountId", folder.AccountId.ToString());
        command.Parameters.AddWithValue("$imapFullName", (object?)folder.ImapFullName ?? DBNull.Value);
        command.Parameters.AddWithValue("$displayName", folder.DisplayName);
        command.Parameters.AddWithValue("$specialUse", (int)folder.SpecialUse);
        command.Parameters.AddWithValue("$parentFolderId", (object?)folder.ParentFolderId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$uidValidity", folder.UidValidity);
        command.Parameters.AddWithValue("$uidNext", folder.UidNext);
        command.Parameters.AddWithValue("$highestModSeq", (object?)folder.HighestModSeq ?? DBNull.Value);
        command.Parameters.AddWithValue("$unreadCount", folder.UnreadCount);
        command.Parameters.AddWithValue("$totalCount", folder.TotalCount);
        command.Parameters.AddWithValue("$lastSyncedAt", (object?)folder.LastSyncedAt?.ToString("o") ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateCountsAsync(Guid folderId, int unreadCount, int totalCount, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE folders SET unread_count = $unreadCount, total_count = $totalCount WHERE id = $id;";
        command.Parameters.AddWithValue("$unreadCount", unreadCount);
        command.Parameters.AddWithValue("$totalCount", totalCount);
        command.Parameters.AddWithValue("$id", folderId.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private static MailFolder Map(SqliteDataReader reader)
    {
        string? GetNullableString(string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        var parentIdText = GetNullableString("parent_folder_id");
        var lastSyncedText = GetNullableString("last_synced_at");
        var highestModSeqOrdinal = reader.GetOrdinal("highest_mod_seq");

        return new MailFolder
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            AccountId = Guid.Parse(reader.GetString(reader.GetOrdinal("account_id"))),
            ImapFullName = GetNullableString("imap_full_name"),
            DisplayName = reader.GetString(reader.GetOrdinal("display_name")),
            SpecialUse = (MailFolderSpecialUse)reader.GetInt64(reader.GetOrdinal("special_use")),
            ParentFolderId = parentIdText is null ? null : Guid.Parse(parentIdText),
            UidValidity = (uint)reader.GetInt64(reader.GetOrdinal("uid_validity")),
            UidNext = (uint)reader.GetInt64(reader.GetOrdinal("uid_next")),
            HighestModSeq = reader.IsDBNull(highestModSeqOrdinal) ? null : (ulong)reader.GetInt64(highestModSeqOrdinal),
            UnreadCount = (int)reader.GetInt64(reader.GetOrdinal("unread_count")),
            TotalCount = (int)reader.GetInt64(reader.GetOrdinal("total_count")),
            LastSyncedAt = lastSyncedText is null ? null : DateTimeOffset.Parse(lastSyncedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };
    }
}
