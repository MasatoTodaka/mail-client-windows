using MailClient.Core.Abstractions;
using Windows.ApplicationModel.DataTransfer;

namespace MailClient.Platform;

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);
    }
}
