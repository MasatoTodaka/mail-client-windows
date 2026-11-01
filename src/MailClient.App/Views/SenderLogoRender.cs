using System.Collections.Concurrent;
using MailClient.Core.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MailClient.App.Views;

// Decides how a cached sender logo is drawn inside its round avatar slot, because the sharpness
// problem with non-official logos is mostly in how they're drawn, not in what was downloaded:
// most sites only publish a 32-64px favicon, and stretching that to fill a 40 DIP circle (60-80
// physical pixels on a typical display) upscales it into mush. Instead:
//   * Fill  -- the image has enough pixels for the slot (or is a vector): it fills the circle, and
//              a bigger-than-needed raster is decoded straight to the slot's pixel size, so it's
//              downscaled once with a proper filter rather than left to the brush.
//   * Badge -- a small raster is shown at (about) its native pixel size, centered on the white
//              backing. A 32px icon drawn at 32 physical pixels is crisp; drawn at 80 it is not.
public static class SenderLogoRender
{
    public const double ListAvatarDip = 40;
    public const double ReadingPaneAvatarDip = 36;

    // The icon must sit inside the circle even when it's a square with opaque corners: a square of
    // side s has its corners s/sqrt(2) from the center, so s <= ~28 keeps them within a 40 DIP circle.
    private const double MaxBadgeDip = 28;
    private const double MinBadgeDip = 16;

    // Share of the slot's physical pixels an image must cover to count as "enough" to fill it.
    private const double FillThreshold = 0.85;

    // Display scale (RasterizationScale) of the window the avatars are drawn in; MainWindow keeps
    // this current. Used only to convert between DIPs and physical pixels.
    public static double Scale { get; set; } = 1.0;

    public sealed record Info(ImageSource Source, bool Fill, double BadgeDip);

    // Decoded sources are kept per (file, size, scale): list rows are recycled and rebound
    // constantly while scrolling, and re-reading and re-decoding the same few dozen logo files for
    // every bind was a large part of the list's scroll cost. Cache files are write-once, so an
    // entry never goes stale. Only touched from the UI thread in practice; concurrent for safety.
    private static readonly ConcurrentDictionary<(string Path, double Dip, double Scale), Info?> Cache = new();

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
                return new Info(new SvgImageSource(uri), Fill: true, MaxBadgeDip);

            // Unknown formats (e.g. WebP) can't be measured here; let the decoder have a go and fill.
            if (width <= 0)
                return new Info(new BitmapImage(uri), Fill: true, MaxBadgeDip);

            var slotPixels = avatarDip * scale;
            if (width >= slotPixels * FillThreshold)
            {
                var bitmap = new BitmapImage();
                if (width > slotPixels)
                    bitmap.DecodePixelWidth = (int)Math.Ceiling(slotPixels);
                bitmap.UriSource = uri;
                return new Info(bitmap, Fill: true, MaxBadgeDip);
            }

            var badgeDip = Math.Clamp(width / scale, MinBadgeDip, MaxBadgeDip);
            return new Info(new BitmapImage(uri), Fill: false, badgeDip);
        }
        catch
        {
            return null;
        }
    }
}
