using MailClient.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MailClient.App.Views.Converters;

// Purely local/synchronous — only checks whether a logo is ALREADY cached on disk, never makes a
// network call. This is what x:Bind (not a one-time Loaded handler) needs: it's re-evaluated on
// every rebind, including a virtualized ListView recycling a container for a different row, so
// it can never show a logo left over from whichever sender that container previously displayed.
// Actual fetching happens in the background via MessageListViewModel's prefetch, which triggers
// a list refresh once new logos land in the cache.
public sealed class SenderLogoImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string emailAddress || string.IsNullOrWhiteSpace(emailAddress))
            return null!;

        // Delegates the domain-matching/cache-path logic to ISenderLogoService rather than
        // duplicating it here — this file previously rebuilt the domain and filename itself, which
        // silently went stale (still looking for the old "{domain}.png" name) the last time that
        // logic changed in SenderLogoService.
        var senderLogoService = App.Services.GetRequiredService<ISenderLogoService>();
        var path = senderLogoService.GetCachedLogoPath(emailAddress);
        return (object?)CreateImageSource(path) ?? null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    // Shared with ReadingPaneView's code-behind logo update (which can't use an x:Bind converter
    // since it isn't driven by a DataTemplate). Simple Icons entries are cached as .svg;
    // Google-favicon fallbacks as .png -- SvgImageSource rasterizes on demand at whatever size the
    // element actually renders at, so an .svg source never blurs or shows clip-edge aliasing the
    // way a small upscaled .png can.
    public static ImageSource? CreateImageSource(string? path)
    {
        if (path is null)
            return null;

        return Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)
            ? new SvgImageSource(new Uri(path))
            : new BitmapImage(new Uri(path));
    }
}
