using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

// Wraps a single account's IMAP connection (implemented over MailKit in MailClient.Mail).
// One instance per connection attempt — resolve a fresh instance per account, don't share.
public interface IImapAccountClient : IDisposable
{
    Task ConnectAsync(Account account, string password, CancellationToken ct);
    Task DisconnectAsync();
    bool SupportsIdle { get; }

    Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken ct);
    Task<(uint UidValidity, uint UidNext)> SelectFolderAsync(string imapFullName, CancellationToken ct);

    // Lightweight IMAP STATUS query (no message fetch) — for badging folders the user hasn't
    // opened yet with accurate total/unread counts.
    Task<(int TotalCount, int UnreadCount)> GetFolderStatusAsync(string imapFullName, CancellationToken ct);

    // Lightweight fetch of just INTERNALDATE (server-received time) for an explicit UID set —
    // for backfilling already-cached messages' sort date without re-fetching full headers.
    Task<IReadOnlyDictionary<uint, DateTimeOffset>> FetchInternalDatesAsync(
        string imapFullName, IReadOnlyList<uint> uids, CancellationToken ct);

    Task<IReadOnlyList<MailMessage>> FetchHeadersAsync(
        string imapFullName, uint fromUid, uint? toUid, int maxCount, CancellationToken ct);

    Task<(string? TextBody, string? HtmlBody)> FetchBodyAsync(
        string imapFullName, uint uid, CancellationToken ct);

    Task SetFlagsAsync(string imapFullName, uint uid, bool isRead, bool isFlagged, CancellationToken ct);
    Task MoveAsync(string imapFullName, uint uid, string destinationFullName, CancellationToken ct);
    Task DeleteAsync(string imapFullName, uint uid, CancellationToken ct);
    Task AppendAsync(string imapFullName, byte[] mimeMessage, CancellationToken ct);
}
