using System.Globalization;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.Core.Text;
using Microsoft.Data.Sqlite;

namespace MailClient.Data.Repositories;

public sealed class MessageRepository(MailDbContext db) : IMessageStore
{
    public async Task<IReadOnlyList<MailMessage>> GetPageAsync(Guid folderId, int skip, int take, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM messages WHERE folder_id = $folderId ORDER BY date DESC LIMIT $take OFFSET $skip;";
        command.Parameters.AddWithValue("$folderId", folderId.ToString());
        command.Parameters.AddWithValue("$take", take);
        command.Parameters.AddWithValue("$skip", skip);

        var messages = new List<MailMessage>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            messages.Add(Map(reader));
        return messages;
    }

    public async Task<MailMessage?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM messages WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<MailMessage?> GetByUidAsync(Guid folderId, uint uid, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM messages WHERE folder_id = $folderId AND uid = $uid;";
        command.Parameters.AddWithValue("$folderId", folderId.ToString());
        command.Parameters.AddWithValue("$uid", uid);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<uint?> GetMinUidAsync(Guid folderId, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(uid) FROM messages WHERE folder_id = $folderId;";
        command.Parameters.AddWithValue("$folderId", folderId.ToString());
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (uint)Convert.ToInt64(result);
    }

    public async Task<(int Total, int Unread)> GetFolderCountsAsync(Guid folderId, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), SUM(CASE WHEN is_read = 0 THEN 1 ELSE 0 END)
            FROM messages WHERE folder_id = $folderId;
            """;
        command.Parameters.AddWithValue("$folderId", folderId.ToString());
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return (0, 0);

        var total = (int)reader.GetInt64(0);
        var unread = reader.IsDBNull(1) ? 0 : (int)reader.GetInt64(1);
        return (total, unread);
    }

    public async Task<int> FixMojibakeSubjectsAsync(CancellationToken ct)
    {
        var toFix = new List<(string Id, string Subject)>();

        await using (var connection = db.CreateConnection())
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, subject FROM messages;";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetString(0);
                var subject = reader.GetString(1);
                var fixedSubject = MojibakeFixer.Fix(subject);
                if (fixedSubject != subject)
                    toFix.Add((id, fixedSubject));
            }
        }

        if (toFix.Count == 0)
            return 0;

        await using var updateConnection = db.CreateConnection();
        await updateConnection.OpenAsync(ct);
        foreach (var (id, fixedSubject) in toFix)
        {
            await using var command = updateConnection.CreateCommand();
            command.CommandText = "UPDATE messages SET subject = $subject WHERE id = $id;";
            command.Parameters.AddWithValue("$subject", fixedSubject);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(ct);
        }

        return toFix.Count;
    }

    public async Task<IReadOnlyList<MailMessage>> GetDownloadedBodyMessagesAsync(CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM messages WHERE is_body_downloaded = 1;";

        var messages = new List<MailMessage>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            messages.Add(Map(reader));
        return messages;
    }

    public async Task SaveAsync(MailMessage message, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO messages (
                id, account_id, folder_id, uid, message_id, in_reply_to, references_header,
                subject, from_display, from_address, to_recipients, cc_recipients, date, snippet,
                is_read, is_flagged, is_answered, is_draft, has_attachments, size,
                is_body_downloaded, body_text_path, body_html_path)
            VALUES (
                $id, $accountId, $folderId, $uid, $messageId, $inReplyTo, $references,
                $subject, $fromDisplay, $fromAddress, $toRecipients, $ccRecipients, $date, $snippet,
                $isRead, $isFlagged, $isAnswered, $isDraft, $hasAttachments, $size,
                $isBodyDownloaded, $bodyTextPath, $bodyHtmlPath)
            ON CONFLICT(id) DO UPDATE SET
                account_id = excluded.account_id,
                folder_id = excluded.folder_id,
                uid = excluded.uid,
                message_id = excluded.message_id,
                in_reply_to = excluded.in_reply_to,
                references_header = excluded.references_header,
                subject = excluded.subject,
                from_display = excluded.from_display,
                from_address = excluded.from_address,
                to_recipients = excluded.to_recipients,
                cc_recipients = excluded.cc_recipients,
                date = excluded.date,
                snippet = excluded.snippet,
                is_read = excluded.is_read,
                is_flagged = excluded.is_flagged,
                is_answered = excluded.is_answered,
                is_draft = excluded.is_draft,
                has_attachments = excluded.has_attachments,
                size = excluded.size,
                is_body_downloaded = excluded.is_body_downloaded,
                body_text_path = excluded.body_text_path,
                body_html_path = excluded.body_html_path;
            """;
        command.Parameters.AddWithValue("$id", message.Id.ToString());
        command.Parameters.AddWithValue("$accountId", message.AccountId.ToString());
        command.Parameters.AddWithValue("$folderId", message.FolderId.ToString());
        command.Parameters.AddWithValue("$uid", message.Uid);
        command.Parameters.AddWithValue("$messageId", (object?)message.MessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$inReplyTo", (object?)message.InReplyTo ?? DBNull.Value);
        command.Parameters.AddWithValue("$references", (object?)message.References ?? DBNull.Value);
        command.Parameters.AddWithValue("$subject", message.Subject);
        command.Parameters.AddWithValue("$fromDisplay", message.FromDisplay);
        command.Parameters.AddWithValue("$fromAddress", message.FromAddress);
        command.Parameters.AddWithValue("$toRecipients", message.ToRecipients);
        command.Parameters.AddWithValue("$ccRecipients", (object?)message.CcRecipients ?? DBNull.Value);
        command.Parameters.AddWithValue("$date", message.Date.ToString("o"));
        command.Parameters.AddWithValue("$snippet", message.Snippet);
        command.Parameters.AddWithValue("$isRead", message.IsRead);
        command.Parameters.AddWithValue("$isFlagged", message.IsFlagged);
        command.Parameters.AddWithValue("$isAnswered", message.IsAnswered);
        command.Parameters.AddWithValue("$isDraft", message.IsDraft);
        command.Parameters.AddWithValue("$hasAttachments", message.HasAttachments);
        command.Parameters.AddWithValue("$size", message.Size);
        command.Parameters.AddWithValue("$isBodyDownloaded", message.IsBodyDownloaded);
        command.Parameters.AddWithValue("$bodyTextPath", (object?)message.BodyTextPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$bodyHtmlPath", (object?)message.BodyHtmlPath ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM messages WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SetReadAsync(Guid id, bool isRead, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE messages SET is_read = $isRead WHERE id = $id;";
        command.Parameters.AddWithValue("$isRead", isRead);
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SetFlaggedAsync(Guid id, bool isFlagged, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE messages SET is_flagged = $isFlagged WHERE id = $id;";
        command.Parameters.AddWithValue("$isFlagged", isFlagged);
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private static MailMessage Map(SqliteDataReader reader)
    {
        string? GetNullableString(string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        bool GetBool(string column) => reader.GetInt64(reader.GetOrdinal(column)) != 0;

        return new MailMessage
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            AccountId = Guid.Parse(reader.GetString(reader.GetOrdinal("account_id"))),
            FolderId = Guid.Parse(reader.GetString(reader.GetOrdinal("folder_id"))),
            Uid = (uint)reader.GetInt64(reader.GetOrdinal("uid")),
            MessageId = GetNullableString("message_id"),
            InReplyTo = GetNullableString("in_reply_to"),
            References = GetNullableString("references_header"),
            Subject = reader.GetString(reader.GetOrdinal("subject")),
            FromDisplay = reader.GetString(reader.GetOrdinal("from_display")),
            FromAddress = reader.GetString(reader.GetOrdinal("from_address")),
            ToRecipients = reader.GetString(reader.GetOrdinal("to_recipients")),
            CcRecipients = GetNullableString("cc_recipients"),
            Date = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("date")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Snippet = reader.GetString(reader.GetOrdinal("snippet")),
            IsRead = GetBool("is_read"),
            IsFlagged = GetBool("is_flagged"),
            IsAnswered = GetBool("is_answered"),
            IsDraft = GetBool("is_draft"),
            HasAttachments = GetBool("has_attachments"),
            Size = (int)reader.GetInt64(reader.GetOrdinal("size")),
            IsBodyDownloaded = GetBool("is_body_downloaded"),
            BodyTextPath = GetNullableString("body_text_path"),
            BodyHtmlPath = GetNullableString("body_html_path"),
        };
    }
}
