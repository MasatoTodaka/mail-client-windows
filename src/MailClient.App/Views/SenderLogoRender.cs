using System.Collections.Concurrent;
using MailClient.Core.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MailClient.App.Views;

// Decides whether a cached sender logo is sharp enough to show in its round avatar slot. A logo
// that would have to be noticeably upscaled to fill the circle is NOT shown at all (the colored
// initial avatar stays): most sites only publish a 16-48px favicon, and a blurry logo is worse
// than none. Vector logos always qualify; a bigger-than-needed raster is decoded straight to the
// slot's pixel size, so it's downscaled once with a proper filter rather than left to the brush.
public static class SenderLogoRender
{
    public const double ListAvatarDip = 40;
    public const double ReadingPaneAvatarDip = 36;

    // Share of the slot's physical pixels an image must cover to be shown: at 0.75 the worst case
    // is a ~1.3x upscale, which is still visually clean. Below that the logo is hidden.
    private const double MinSharpness = 0.75;

    // Display scale (RasterizationScale) of the window the avatars are drawn in; MainWindow keeps
    // this current. Used only to convert DIPs to physical pixels.
    public static double Scale { get; set; } = 1.0;

    public sealed record Info(ImageSource Source);

    // Decoded sources are kept per (file, size, scale): list rows are recycled and rebound
    // constantly while scrolling, and re-reading and re-decoding the same few dozen logo files for
    // every bind was a large part of the list's scroll cost. Cache files are write-once, so an
    // entry never goes stale. A null entry (too small to show) is cached too, so it isn't re-probed.
    private static readonly ConcurrentDictionary<(string Path, double Dip, double Scale), Info?> Cache = new();

    // Null when there is no logo or it isn't sharp enough -- callers keep the initial avatar.
    public static Info? For(string? path, double avatarDip)
    {
        if (path is null)
            return null;

        return Cache.GetOrAdd((path, avatarDip, Scale), key => Create(key.Path, key.Dip, key.Scale));
    }

    private static Info? Create(string path, double avatarDip, double scale)
    {
        try
        {
            var uri = new Uri(path);
            var (kind, width) = ImageProbe.Probe(File.ReadAllBytes(path));

            if (kind == ImageKind.Svg)
                return new Info(new SvgImageSource(uri));

            // Unknown formats can't be measured, so their sharpness can't be vouched for.
            if (kind == ImageKind.Unknown)
                return null;

            var slotPixels = avatarDip * scale;
            if (width < slotPixels * MinSharpness)
                return null;

            var bitmap = new BitmapImage();
            if (width > slotPixels)
                bitmap.DecodePixelWidth = (int)Math.Ceiling(slotPixels);
            bitmap.UriSource = uri;
            return new Info(bitmap);
        }
        catch
        {
            return null;
        }
    }
}
