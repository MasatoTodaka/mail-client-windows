using System.Globalization;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using Microsoft.Data.Sqlite;

namespace MailClient.Data.Search;

public sealed class FtsSearchIndex(MailDbContext db) : ISearchIndex
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, Guid? accountId, int limit, CancellationToken ct)
    {
        var matchExpression = BuildMatchExpression(query);
        if (matchExpression is null)
            return [];

        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.*, snippet(messages_fts, -1, '[', ']', '...', 10) AS fts_snippet
            FROM messages_fts
            JOIN messages m ON m.rowid = messages_fts.rowid
            WHERE messages_fts MATCH $match
              AND ($accountId IS NULL OR m.account_id = $accountId)
            ORDER BY bm25(messages_fts)
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$match", matchExpression);
        command.Parameters.AddWithValue("$accountId", accountId is null ? DBNull.Value : accountId.Value.ToString());
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<SearchResult>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new SearchResult(MapMessage(reader), reader.GetString(reader.GetOrdinal("fts_snippet"))));
        }
        return results;
    }

    // Each whitespace-separated token becomes a quoted prefix match, ANDed together.
    // Quoting avoids FTS5 query-syntax errors on user input containing '-', '"', etc.
    private static string? BuildMatchExpression(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return null;

        return string.Join(" AND ", tokens.Select(t => $"\"{t.Replace("\"", "\"\"")}\"*"));
    }

    private static MailMessage MapMessage(SqliteDataReader reader)
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
