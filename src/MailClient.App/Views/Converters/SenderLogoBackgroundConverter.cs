using MailClient.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MailClient.App.Views.Converters;

// Paired with SenderLogoImageConverter: gives the logo overlay an opaque white backing, but only
// when a real logo is actually cached for this sender. Many favicons have a transparent
// background around the mark itself, and without an opaque backing behind it, the colored-initial
// fallback Border underneath showed through those transparent regions -- looking like the logo
// and the initial were overlapping/colliding rather than one cleanly replacing the other. When no
// logo is cached, this returns null (transparent) so the colored-initial fallback still shows
// through cleanly on its own, same as before.
public sealed class SenderLogoBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string emailAddress || string.IsNullOrWhiteSpace(emailAddress))
            return null!;

        var senderLogoService = App.Services.GetRequiredService<ISenderLogoService>();
        return senderLogoService.GetCachedLogoPath(emailAddress) is not null
            ? new SolidColorBrush(Colors.White)
            : null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
