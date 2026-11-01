using System.Text;
using MailClient.Core.Imaging;
using Xunit;

namespace MailClient.Infrastructure.Tests;

// ImageProbe decides, from header bytes alone, how a cached sender logo is drawn (fill the avatar
// circle vs. a native-size badge), so a wrong width here means a blurry or tiny logo.
public sealed class ImageProbeTests
{
    [Fact]
    public void Png_ReportsWidthFromIhdr()
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        png[19] = 180; // IHDR width (big-endian) at offset 16
        Assert.Equal((ImageKind.Png, 180), ImageProbe.Probe(png));
    }

    [Fact]
    public void Png_WiderThan255_UsesAllFourBytes()
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        png[18] = 0x01;
        png[19] = 0x00; // 256
        Assert.Equal((ImageKind.Png, 256), ImageProbe.Probe(png));
    }

    [Fact]
    public void Gif_ReportsLittleEndianWidth()
    {
        var gif = new byte[13];
        Encoding.ASCII.GetBytes("GIF89a").CopyTo(gif, 0);
        gif[6] = 0x40; // 64
        Assert.Equal((ImageKind.Gif, 64), ImageProbe.Probe(gif));
    }

    [Fact]
    public void Jpeg_SkipsNonFrameSegmentsToFindTheSof()
    {
        // SOI, an APP0 segment (length 4: two payload bytes), then SOF0 with width 0x0080 (128).
        byte[] jpeg =
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x40, 0x00, 0x80, 0x03,
        ];
        Assert.Equal((ImageKind.Jpeg, 128), ImageProbe.Probe(jpeg));
    }

    [Fact]
    public void Ico_ReportsTheWidestFrame_AndZeroMeans256()
    {
        var ico = new byte[6 + 16 * 3];
        ico[2] = 1; // type: icon
        ico[4] = 3; // three frames
        ico[6] = 16;
        ico[6 + 16] = 32;
        ico[6 + 32] = 0; // 0 => 256
        Assert.Equal((ImageKind.Ico, 256), ImageProbe.Probe(ico));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"></svg>")]
    [InlineData("﻿<?xml version=\"1.0\"?>\n<svg width=\"10\" height=\"10\"/>")]
    public void Svg_IsTreatedAsVector(string svg)
    {
        Assert.Equal((ImageKind.Svg, ImageProbe.VectorWidth), ImageProbe.Probe(Encoding.UTF8.GetBytes(svg)));
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>404 Not Found</body></html>")]
    [InlineData("not an image at all")]
    [InlineData("")]
    public void HtmlErrorPagesAndGarbage_AreUnknown(string content)
    {
        // A server can answer an icon URL with a 200 and an HTML page; that must not be cached as a logo.
        Assert.Equal((ImageKind.Unknown, 0), ImageProbe.Probe(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void TruncatedHeaders_DoNotThrow()
    {
        Assert.Equal(ImageKind.Unknown, ImageProbe.Probe(new byte[] { 0x89, 0x50, 0x4E }).Kind);
        Assert.Equal(ImageKind.Unknown, ImageProbe.Probe(new byte[] { 0xFF }).Kind);
    }
}
