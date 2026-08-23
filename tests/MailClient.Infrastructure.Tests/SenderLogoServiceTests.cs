using System.Reflection;
using System.Text;
using Xunit;

namespace MailClient.Infrastructure.Tests;

// Targets SenderLogoService's private static helpers directly via reflection rather than widening
// their visibility (they have no external dependency, so testing them in isolation doesn't need
// an instance — only Type.GetMethod + Invoke(null, ...)). Every case here reproduces a real bug
// found and fixed in production this session; see the project memory for the full incident
// history each one is drawn from.
public sealed class SenderLogoServiceTests
{
    private static readonly Type ServiceType = typeof(SenderLogoService);

    [Theory]
    // Ordinary two-label domains pass through unchanged.
    [InlineData("example.com", "example.com")]
    // co.jp (and the other recognized multi-label suffixes) keep three labels.
    [InlineData("amazon.co.jp", "amazon.co.jp")]
    // The actual bug this heuristic was added for: mail-sending subdomains of co.jp-style sites
    // must collapse to the registrable domain so the favicon/Simple Icons/BIMI lookups can match
    // the company's real site instead of a subdomain nobody publishes a favicon for.
    [InlineData("emagazine.rakuten.co.jp", "rakuten.co.jp")]
    [InlineData("ml.club.kfc.co.jp", "kfc.co.jp")]
    [InlineData("point.recruit.co.jp", "recruit.co.jp")]
    // A suffix NOT in the recognized set (an ordinary two-label TLD like .com) only keeps the last
    // two labels — this is the case that must NOT over-collapse a legitimate subdomain-shaped
    // company domain like PayPay Card's mail-sending host down past what BIMI actually needs.
    [InlineData("mail.paypay-card.co.jp", "paypay-card.co.jp")]
    [InlineData("mail.yahoo.co.jp", "yahoo.co.jp")]
    [InlineData("em.riotgames.com", "riotgames.com")]
    public void ToRegistrableDomain_CollapsesSubdomainsToTheRegistrableDomain(string input, string expected)
    {
        Assert.Equal(expected, InvokeStatic<string>("ToRegistrableDomain", input));
    }

    [Theory]
    [InlineData("user@example.com", "example.com")]
    [InlineData("user@sub.example.com", "example.com")]
    [InlineData("USER@EXAMPLE.COM", "example.com")]
    [InlineData("not-an-email", null)]
    [InlineData("trailing-at@", null)]
    public void ExtractDomain_NormalizesToRegistrableDomain(string emailAddress, string? expected)
    {
        Assert.Equal(expected, InvokeStatic<string?>("ExtractDomain", emailAddress));
    }

    [Fact]
    public void ExtractRawDomain_DoesNotCollapseSubdomains()
    {
        // The whole point of splitting this out from ExtractDomain: BIMI has to be queried at the
        // exact sending domain first (PayPay Card, Yahoo, and Riot Games all publish their BIMI
        // record only at the subdomain, not the registrable domain) before falling back to the
        // registrable domain.
        Assert.Equal("mail.paypay-card.co.jp", InvokeStatic<string?>("ExtractRawDomain", "info@mail.paypay-card.co.jp"));
    }

    [Fact]
    public void ParseBimiLogoUrl_ExtractsTheLField()
    {
        var record = "v=BIMI1; l=https://example.com/logo.svg; a=https://example.com/vmc.pem";
        Assert.Equal("https://example.com/logo.svg", InvokeStatic<string?>("ParseBimiLogoUrl", record));
    }

    [Fact]
    public void ParseBimiLogoUrl_HandlesFieldsWithoutSpacesAfterSemicolons()
    {
        // Real records vary in whitespace style (Amazon's and eBay's omit the space after ';',
        // PayPal's and LinkedIn's include it) — both must parse.
        var record = "v=BIMI1;l=https://example.com/logo.svg;a=https://example.com/vmc.pem";
        Assert.Equal("https://example.com/logo.svg", InvokeStatic<string?>("ParseBimiLogoUrl", record));
    }

    [Fact]
    public void ParseBimiLogoUrl_RejectsNonHttpsLogoUrl()
    {
        var record = "v=BIMI1; l=http://example.com/logo.svg; a=https://example.com/vmc.pem";
        Assert.Null(InvokeStatic<string?>("ParseBimiLogoUrl", record));
    }

    [Fact]
    public void ParseBimiLogoUrl_ReturnsNullWhenLFieldMissing()
    {
        var record = "v=BIMI1; a=https://example.com/vmc.pem";
        Assert.Null(InvokeStatic<string?>("ParseBimiLogoUrl", record));
    }

    [Fact]
    public void SanitizeSvg_RemovesFillNoneFromGroupElements()
    {
        // Reproduces the exact structure that made Domino's Pizza's real BIMI logo render as a
        // blank white circle in WinUI: a wrapping <g fill="none"> whose child <path> supplies its
        // own color, which WinUI's SvgImageSource didn't correctly let win.
        var svg = "<svg><g id=\"x\" fill=\"none\"><path fill=\"#E21836\" d=\"M0 0\"/></g></svg>";
        var sanitized = Encoding.UTF8.GetString(InvokeStaticBytes("SanitizeSvg", Encoding.UTF8.GetBytes(svg)));

        Assert.DoesNotContain("fill=\"none\"", sanitized);
        Assert.Contains("fill=\"#E21836\"", sanitized); // the actual color must survive
    }

    [Fact]
    public void SanitizeSvg_RemovesPxUnitWidthAndHeightOnTheRootElement()
    {
        var svg = "<svg width=\"200px\" height=\"200px\" viewBox=\"0 0 200 200\"></svg>";
        var sanitized = Encoding.UTF8.GetString(InvokeStaticBytes("SanitizeSvg", Encoding.UTF8.GetBytes(svg)));

        Assert.DoesNotContain("width=\"200px\"", sanitized);
        Assert.DoesNotContain("height=\"200px\"", sanitized);
        Assert.Contains("viewBox=\"0 0 200 200\"", sanitized); // untouched
    }

    [Fact]
    public void SanitizeSvg_LeavesUnitlessWidthHeightAndOtherFillNoneUsageAlone()
    {
        // Guards against an overly broad regex: a stroke-only shape legitimately using
        // fill="none" on a <path> (not a <g>) must survive, and width/height without a "px" unit
        // must survive untouched too.
        var svg = "<svg width=\"200\" height=\"200\"><path fill=\"none\" stroke=\"#000\" d=\"M0 0\"/></svg>";
        var sanitized = Encoding.UTF8.GetString(InvokeStaticBytes("SanitizeSvg", Encoding.UTF8.GetBytes(svg)));

        Assert.Contains("width=\"200\"", sanitized);
        Assert.Contains("height=\"200\"", sanitized);
        Assert.Contains("fill=\"none\"", sanitized);
    }

    private static T InvokeStatic<T>(string methodName, params object?[] args)
    {
        var method = ServiceType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{methodName} not found — has it been renamed?");
        return (T)method.Invoke(null, args)!;
    }

    private static byte[] InvokeStaticBytes(string methodName, byte[] arg)
    {
        var method = ServiceType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{methodName} not found — has it been renamed?");
        return (byte[])method.Invoke(null, [arg])!;
    }
}
