using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

public interface INotificationService
{
    void Register();
    void ShowNewMailNotification(MailMessage message, Account account);

    // Raised (possibly off the UI thread) when the user clicks a new-mail toast, carrying the
    // local message id embedded in the notification's arguments.
    event EventHandler<Guid>? NotificationInvoked;
}
