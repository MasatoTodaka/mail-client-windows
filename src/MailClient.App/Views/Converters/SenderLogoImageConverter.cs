using MailClient.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Data;

namespace MailClient.App.Views.Converters;

// Purely local/synchronous — only checks whether a logo is ALREADY cached on disk, never makes a
// network call. This is what x:Bind (not a one-time Loaded handler) needs: it's re-evaluated on
// every rebind, including a virtualized ListView recycling a container for a different row, so
// it can never show a logo left over from whichever sender that container previously displayed.
// Actual fetching happens in the background via MessageListViewModel's prefetch, which triggers
// a list refresh once new logos land in the cache.
//
// Returns null both when no logo is cached and when the cached one is too low-resolution to look
// good (see SenderLogoRender) -- either way the row keeps its colored-initial avatar.
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
        return SenderLogoRender.For(senderLogoService.GetCachedLogoPath(emailAddress), SenderLogoRender.ListAvatarDip)?.Source!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
