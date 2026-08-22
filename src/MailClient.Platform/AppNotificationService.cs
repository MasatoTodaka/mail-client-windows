using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace MailClient.Platform;

// Toast notifications via Windows App SDK's AppNotificationManager. Requires package identity,
// which this app has (MSIX) — same reason CredentialLockerStore lives here.
public sealed class AppNotificationService : INotificationService
{
    private const string MessageIdArgument = "messageId";

    public event EventHandler<Guid>? NotificationInvoked;

    public void Register()
    {
        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        AppNotificationManager.Default.Register();
    }

    public void ShowNewMailNotification(MailMessage message, Account account)
    {
        var subject = string.IsNullOrWhiteSpace(message.Subject) ? "(件名なし)" : message.Subject;
        var sender = string.IsNullOrWhiteSpace(message.FromDisplay) ? message.FromAddress : message.FromDisplay;

        var notification = new AppNotificationBuilder()
            .AddArgument(MessageIdArgument, message.Id.ToString())
            .AddText(sender)
            .AddText(subject)
            .AddText(account.DisplayName)
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);
    }

    public void ShowOtpCopiedNotification()
    {
        var notification = new AppNotificationBuilder()
            .AddText("ワンタイムパスワードをコピーしました")
            .AddText("クリップボードに貼り付けできます。")
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue(MessageIdArgument, out var idText) && Guid.TryParse(idText, out var messageId))
            NotificationInvoked?.Invoke(this, messageId);
    }
}
