using MailClient.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Data;
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

        var at = emailAddress.LastIndexOf('@');
        if (at < 0 || at == emailAddress.Length - 1)
            return null!;

        var domain = emailAddress[(at + 1)..].Trim().ToLowerInvariant();
        var appDataPaths = App.Services.GetRequiredService<AppDataPaths>();
        // Filename must match SenderLogoService's cache naming (domain + requested-size suffix) —
        // see the comment on SenderLogoService.SizeSuffix for why the size is baked into the name.
        var path = Path.Combine(appDataPaths.LogosDirectory, $"{domain}-256.png");
        return File.Exists(path) ? new BitmapImage(new Uri(path)) : null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
