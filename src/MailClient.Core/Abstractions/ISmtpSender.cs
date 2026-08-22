using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface ISmtpSender
{
    Task SendAsync(Account account, string password, byte[] mimeMessage, CancellationToken ct);
}
