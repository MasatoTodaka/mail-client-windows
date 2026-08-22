using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

// Wraps a single account's IMAP connection (implemented over MailKit in MailClient.Mail).
public interface IImapAccountClient
{
    Task ConnectAsync(Account account, string password, CancellationToken ct);
    Task DisconnectAsync();
    bool SupportsIdle { get; }

    Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken ct);
    Task<(uint UidValidity, uint UidNext)> SelectFolderAsync(string imapFullName, CancellationToken ct);

    Task<IReadOnlyList<MailMessage>> FetchHeadersAsync(
        string imapFullName, uint fromUid, uint? toUid, int maxCount, CancellationToken ct);

    Task<(string? TextBody, string? HtmlBody)> FetchBodyAsync(
        string imapFullName, uint uid, CancellationToken ct);

    Task SetFlagsAsync(string imapFullName, uint uid, bool isRead, bool isFlagged, CancellationToken ct);
    Task MoveAsync(string imapFullName, uint uid, string destinationFullName, CancellationToken ct);
    Task DeleteAsync(string imapFullName, uint uid, CancellationToken ct);
    Task AppendAsync(string imapFullName, byte[] mimeMessage, CancellationToken ct);
}
