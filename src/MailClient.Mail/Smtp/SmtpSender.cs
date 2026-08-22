using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MailClient.Mail.Smtp;

public sealed class SmtpSender : ISmtpSender
{
    public async Task SendAsync(Account account, string password, byte[] mimeMessage, CancellationToken ct)
    {
        using var stream = new MemoryStream(mimeMessage);
        var message = await MimeMessage.LoadAsync(stream, ct);

        var options = account.SmtpSecurity switch
        {
            SecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SecurityMode.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None,
        };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(account.SmtpHost, account.SmtpPort, options, ct);
            await client.AuthenticateAsync(account.SmtpUsername, password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (AuthenticationException ex)
        {
            throw new SmtpAuthenticationException("SMTP認証に失敗しました。ユーザー名またはパスワードを確認してください。", ex);
        }
        catch (Exception ex) when (ex is not SmtpAuthenticationException and not OperationCanceledException)
        {
            throw new SmtpConnectionException($"SMTPサーバーに接続または送信できませんでした: {ex.Message}", ex);
        }
    }
}
