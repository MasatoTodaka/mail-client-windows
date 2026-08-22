using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface INotificationService
{
    void Register();
    void ShowNewMailNotification(MailMessage message, Account account);
}
