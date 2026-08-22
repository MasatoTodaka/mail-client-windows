using MailClient.Core.Models;

namespace MailClient.Core.Events;

public sealed class MessageArrivedEventArgs(MailMessage message) : EventArgs
{
    public MailMessage Message { get; } = message;
}
