namespace MailClient.Core.Imaging;

public enum ImageKind
{
    Unknown,
    Png,
    Jpeg,
    Gif,
    Ico,
    Svg,
}

// Header-only sniffing of a downloaded icon: what format it really is (a server can answer an icon
// URL with an HTML error page and a 200) and how many pixels wide its largest frame is. Used to
// pick the sharpest available sender logo and to decide how far to downscale it when decoding.
// Deliberately dependency-free: no imaging library is needed just to read a few header bytes.
public static class ImageProbe
{
    // A vector image has no fixed resolution; report "bigger than any raster" so it always wins a
    // sharpness comparison.
    public const int VectorWidth = int.MaxValue;

    public static (ImageKind Kind, int Width) Probe(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return (ImageKind.Png, ReadBigEndianInt32(data[16..]));

        if (data.Length >= 10 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            return (ImageKind.Gif, data[6] | (data[7] << 8));

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
            return (ImageKind.Jpeg, ProbeJpegWidth(data));

        if (data.Length >= 6 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0)
            return (ImageKind.Ico, ProbeIcoWidth(data));

        if (LooksLikeSvg(data))
            return (ImageKind.Svg, VectorWidth);

        return (ImageKind.Unknown, 0);
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> data) =>
        (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];

    // Walks the JPEG marker segments to the first start-of-frame (SOFn) marker, which holds the size.
    private static int ProbeJpegWidth(ReadOnlySpan<byte> data)
    {
        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = data[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            // SOF0-SOF15, except DHT (C4), JPG (C8) and DAC (CC), which share the range but aren't frames.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                return (data[i + 7] << 8) | data[i + 8];

            var segmentLength = (data[i + 2] << 8) | data[i + 3];
            i += 2 + Math.Max(segmentLength, 2);
        }

        return 0;
    }

    // An ICO directory lists every embedded frame; the widest one is what a decoder can offer.
    // A stored width of 0 means 256.
    private static int ProbeIcoWidth(ReadOnlySpan<byte> data)
    {
        var count = data[4] | (data[5] << 8);
        var widest = 0;
        for (var n = 0; n < count; n++)
        {
            var entry = 6 + n * 16;
            if (entry + 16 > data.Length)
                break;

            var width = data[entry] == 0 ? 256 : data[entry];
            widest = Math.Max(widest, width);
        }

        return widest;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> data)
    {
        var head = data[..Math.Min(data.Length, 2048)];
        var text = System.Text.Encoding.UTF8.GetString(head).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (!text.StartsWith("<", StringComparison.Ordinal))
            return false;

        return text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }
}
