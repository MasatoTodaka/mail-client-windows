using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MailClient.Core;
using MailClient.Core.Abstractions;

namespace MailClient.Infrastructure;

public sealed class SenderLogoService(AppDataPaths appDataPaths, ISettingsStore settingsStore) : ISenderLogoService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly JsonSerializerOptions DohJsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Coalesces concurrent requests for the same domain/slug — many message rows share a sender,
    // and without this each one would kick off its own redundant download.
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new();

    // Priority order: BIMI (the sender's own officially-published vector logo, when they publish
    // one — see FetchAndCacheBimiAsync) > Simple Icons (a curated third-party vector logo for
    // well-known brands) > Google favicon (raster, works for almost anything but caps out around
    // 256px and can blur/alias once displayed). Each tier's own cache file existing is what lets
    // GetCachedLogoPath/IsLogoCached skip straight past every tier below it on future lookups, so
    // a domain only ever pays for a BIMI DNS query + Simple Icons miss once, on first encounter.
    public async Task<string?> GetLogoPathAsync(string emailAddress, CancellationToken ct)
    {
        if (!await settingsStore.GetShowSenderLogosEnabledAsync(ct))
            return null;

        var rawDomain = ExtractRawDomain(emailAddress);
        if (rawDomain is null)
            return null;
        var domain = ToRegistrableDomain(rawDomain);

        Directory.CreateDirectory(appDataPaths.LogosDirectory);

        var bimiPath = BimiCachePath(domain);
        if (File.Exists(bimiPath))
            return bimiPath;

        var bimiResult = await _inFlight.GetOrAdd($"bimi:{domain}", _ => FetchAndCacheBimiAsync(rawDomain, domain, bimiPath, ct));
        if (bimiResult is not null)
            return bimiResult;

        if (SimpleIconsSlugs.TryGetValue(domain, out var slug))
        {
            var svgPath = SvgCachePath(slug);
            if (File.Exists(svgPath))
                return svgPath;

            var svgResult = await _inFlight.GetOrAdd($"svg:{slug}", _ => FetchAndCacheSvgAsync(slug, svgPath, ct));
            if (svgResult is not null)
                return svgResult;
        }

        var faviconPath = FaviconCachePath(domain);
        if (File.Exists(faviconPath))
            return faviconPath;

        return await _inFlight.GetOrAdd(domain, _ => FetchAndCacheFaviconAsync(domain, faviconPath, ct));
    }

    public bool IsLogoCached(string emailAddress) => GetCachedLogoPath(emailAddress) is not null;

    public string? GetCachedLogoPath(string emailAddress)
    {
        var domain = ExtractDomain(emailAddress);
        if (domain is null)
            return null;

        var bimiPath = BimiCachePath(domain);
        if (File.Exists(bimiPath))
            return bimiPath;

        if (SimpleIconsSlugs.TryGetValue(domain, out var slug))
        {
            var svgPath = SvgCachePath(slug);
            if (File.Exists(svgPath))
                return svgPath;
        }

        var faviconPath = FaviconCachePath(domain);
        return File.Exists(faviconPath) ? faviconPath : null;
    }

    // Curated domain → Simple Icons (simpleicons.org) slug map for senders known to have a
    // hand-drawn vector brand icon available — these render crisp at any size (no upscale blur,
    // no clip-edge aliasing) since SvgImageSource rasterizes on demand at the actual display size,
    // unlike the raster favicons FetchAndCacheFaviconAsync falls back to below. Every slug here was
    // verified live against https://cdn.jsdelivr.net/npm/simple-icons@latest/icons/{slug}.svg
    // before being added; a domain with no entry (most senders, especially smaller/regional ones
    // Simple Icons doesn't carry) just uses the existing favicon-based path unchanged.
    //
    // Keys are registrable domains (post-ToRegistrableDomain, matching ExtractDomain's output) —
    // add new entries here as senders come up, not by domain guesswork.
    private static readonly Dictionary<string, string> SimpleIconsSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amazon.com"] = "amazon",
        ["amazon.co.jp"] = "amazon",
        ["google.com"] = "google",
        ["uber.com"] = "uber",
        ["microsoft.com"] = "microsoft",
        ["rakuten.co.jp"] = "rakuten",
        ["deepl.com"] = "deepl",
        ["pagerduty.com"] = "pagerduty",
        ["github.com"] = "github",
        ["anthropic.com"] = "anthropic",
        ["claude.ai"] = "claude",
        ["openai.com"] = "openai",
        ["discord.com"] = "discord",
        ["trustpilot.com"] = "trustpilot",
        ["trustpilotmail.com"] = "trustpilot",
        ["kfc.co.jp"] = "kfc",
        ["paypal.com"] = "paypal",
        ["apple.com"] = "apple",
        ["spotify.com"] = "spotify",
        ["netflix.com"] = "netflix",
        ["instagram.com"] = "instagram",
        ["x.com"] = "x",
        ["twitter.com"] = "x",
        ["linkedin.com"] = "linkedin",
        ["facebook.com"] = "facebook",
        ["slack.com"] = "slack",
        ["notion.so"] = "notion",
        ["figma.com"] = "figma",
        ["adobe.com"] = "adobe",
        ["canva.com"] = "canva",
        ["salesforce.com"] = "salesforce",
        ["hubspot.com"] = "hubspot",
        ["stripe.com"] = "stripe",
        ["zoom.us"] = "zoom",
        ["dropbox.com"] = "dropbox",
        ["nvidia.com"] = "nvidia",
        ["riotgames.com"] = "riotgames",
    };

    // The cache filename bakes in the requested size so that bumping RequestedSize automatically
    // invalidates every previously-cached (lower-resolution) logo instead of silently continuing
    // to serve it forever — a plain "{domain}.png" name would never expire.
    private const int RequestedSize = 256;
    private const string SizeSuffix = "-256";

    private string FaviconCachePath(string domain) => Path.Combine(appDataPaths.LogosDirectory, $"{domain}{SizeSuffix}.png");
    private string SvgCachePath(string slug) => Path.Combine(appDataPaths.LogosDirectory, $"si-{slug}.svg");
    private string BimiCachePath(string domain) => Path.Combine(appDataPaths.LogosDirectory, $"bimi-{domain}.svg");

    // BIMI (Brand Indicators for Message Identification, the mechanism that gives Apple/iOS Mail
    // its crisp verified-sender logos): a domain that wants a logo shown next to its mail publishes
    // a TXT record at default._bimi.{domain} pointing to its own official SVG. Looked up via
    // Google's public DNS-over-HTTPS JSON API rather than a DNS library, since it's a plain HTTPS
    // GET — consistent with every other lookup in this class and avoids a native-DNS dependency.
    //
    // Deliberately simplified vs. the full BIMI spec: does NOT verify the domain enforces DMARC
    // (p=quarantine/reject) or validate the accompanying VMC certificate chain (the "a=" field) —
    // that's what real mail clients use to show the little verified checkmark. Skipping it means a
    // domain could in principle publish a BIMI record without properly authenticating its mail and
    // still get its logo shown here. This app doesn't verify DKIM/DMARC on incoming mail at all
    // today (the From header is trusted as-is either way), so this doesn't meaningfully change the
    // existing trust model — it's a cosmetic lookup, not a security signal.
    //
    // Tries the exact sending domain (rawDomain) before falling back to the registrable domain
    // (registrableDomain): most senders publish BIMI at their registrable domain, but some (PayPay
    // Card, confirmed against a real mailbox — its record lives at mail.paypay-card.co.jp, not
    // paypay-card.co.jp) publish it only at the specific subdomain their mail actually comes from.
    // Either way the fetched logo is cached under the registrable domain's path, matching how
    // Simple Icons/favicon already key by registrable domain.
    private async Task<string?> FetchAndCacheBimiAsync(string rawDomain, string registrableDomain, string path, CancellationToken ct)
    {
        try
        {
            var logoUrl = await TryGetBimiLogoUrlAsync(rawDomain, ct);
            if (logoUrl is null && !string.Equals(rawDomain, registrableDomain, StringComparison.OrdinalIgnoreCase))
                logoUrl = await TryGetBimiLogoUrlAsync(registrableDomain, ct);
            if (logoUrl is null)
                return null;

            var bytes = await Http.GetByteArrayAsync(logoUrl, ct);
            if (bytes.Length == 0)
                return null;

            await File.WriteAllBytesAsync(path, SanitizeSvg(bytes), ct);
            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlight.TryRemove($"bimi:{registrableDomain}", out _);
        }
    }

    private async Task<string?> TryGetBimiLogoUrlAsync(string domain, CancellationToken ct)
    {
        try
        {
            var dohUrl = $"https://dns.google/resolve?name={Uri.EscapeDataString($"default._bimi.{domain}")}&type=TXT";
            var json = await Http.GetStringAsync(dohUrl, ct);
            var response = JsonSerializer.Deserialize<DohResponse>(json, DohJsonOptions);
            var record = response?.Answer?
                .Select(a => a.Data)
                .FirstOrDefault(d => d is not null && d.Contains("BIMI1", StringComparison.OrdinalIgnoreCase));
            return record is null ? null : ParseBimiLogoUrl(record);
        }
        catch
        {
            return null;
        }
    }

    // Worked around a real, reproduced WinUI SvgImageSource rendering bug: a live BIMI logo
    // (Domino's Pizza Japan's, fetched from their own CDN) rendered as a completely blank white
    // circle in this app despite being a well-formed, simple SVG (Adobe Illustrator export, no
    // gradients/clipPaths/masks) — while other BIMI logos (Yamato Transport's, e.g.) rendered
    // fine. Bisected the two differences against a working file and confirmed, by testing on the
    // live app with a full process restart between each change (WinUI/the OS appears to cache a
    // decoded SvgImageSource by file path, so overwriting the same path without restarting
    // silently keeps showing the old decode — a trap when iterating on this):
    //  - a `fill="none"` on the wrapping <g> wasn't being overridden by child <path fill="#..">
    //    elements the way the SVG spec requires
    //  - an explicit `width="200px" height="200px"` (with a literal "px" unit) on the root <svg>,
    //    alongside a viewBox, confused the intrinsic-size calculation
    // Both were verified together to fix the actual broken file; not independently isolated
    // (removing the "px"-unit dimensions alone may already be sufficient), but both are safe,
    // narrowly-scoped, standard SVG-optimizer-style transforms that shouldn't change how any
    // already-working SVG renders.
    private static byte[] SanitizeSvg(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        text = Regex.Replace(text, "(<g\\b[^>]*?)\\s+fill=\"none\"", "$1");
        text = Regex.Replace(text, "\\s+(width|height)=\"[\\d.]+px\"", "");
        return Encoding.UTF8.GetBytes(text);
    }

    // Parses "v=BIMI1; l=https://example.com/logo.svg; a=https://example.com/vmc.pem" (the "a="
    // certificate field is ignored — see the comment on FetchAndCacheBimiAsync). Requires the logo
    // URL to be HTTPS, per the BIMI spec.
    private static string? ParseBimiLogoUrl(string record)
    {
        foreach (var part in record.Trim('"').Split(';'))
        {
            var kv = part.Trim();
            if (!kv.StartsWith("l=", StringComparison.OrdinalIgnoreCase))
                continue;

            var url = kv[2..].Trim();
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? url
                : null;
        }

        return null;
    }

    private sealed class DohResponse
    {
        public int Status { get; set; }
        public DohAnswer[]? Answer { get; set; }
    }

    private sealed class DohAnswer
    {
        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }

    private async Task<string?> FetchAndCacheSvgAsync(string slug, string path, CancellationToken ct)
    {
        try
        {
            var url = $"https://cdn.jsdelivr.net/npm/simple-icons@latest/icons/{Uri.EscapeDataString(slug)}.svg";
            var bytes = await Http.GetByteArrayAsync(url, ct);
            if (bytes.Length == 0)
                return null;

            await File.WriteAllBytesAsync(path, SanitizeSvg(bytes), ct);
            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlight.TryRemove($"svg:{slug}", out _);
        }
    }

    private async Task<string?> FetchAndCacheFaviconAsync(string domain, string path, CancellationToken ct)
    {
        try
        {
            // Google's public favicon endpoint: simple, free, no API key. Falls back to a
            // generic globe icon for domains it doesn't recognize rather than erroring, so this
            // won't always distinguish "no logo" from "unrecognized domain" — acceptable given
            // the caller always has the colored-initial avatar as a visual fallback anyway.
            // Requesting a larger size than the ~34-40px display slot needs still matters: most
            // real favicons aren't natively 64px, so asking for 64 just means Google upscales a
            // smaller source to fill it, and that blur/aliasing was visible once actually
            // displayed. Asking for 256 gets the highest-resolution source Google has for the
            // domain, which then downscales cleanly to the small on-screen size instead.
            // (Verified empirically: asking for even larger, e.g. 512, makes Google give up and
            // return a generic 16x16 fallback instead of its best real size — 256 is the ceiling.)
            var url = $"https://www.google.com/s2/favicons?sz={RequestedSize}&domain={Uri.EscapeDataString(domain)}";
            var bytes = await Http.GetByteArrayAsync(url, ct);
            if (bytes.Length == 0)
                return null;

            await File.WriteAllBytesAsync(path, bytes, ct);
            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlight.TryRemove(domain, out _);
        }
    }

    // co.jp/mail.rakuten.co.jp-style transactional and marketing mail is very often sent from a
    // subdomain of the company's real site (emagazine.rakuten.co.jp, ml.club.kfc.co.jp,
    // point.recruit.co.jp were all observed in a real mailbox) rather than the bare registrable
    // domain (rakuten.co.jp, kfc.co.jp, recruit.co.jp) that actually has a favicon Google can
    // serve (and the one SimpleIconsSlugs above is keyed by). Reducing to the registrable domain
    // before querying turns those into hits instead of falling back to the colored-initial avatar.
    //
    // This is a small heuristic, not a full Public Suffix List: it only recognizes the common
    // multi-label suffixes (co.jp and similar) that showed up in practice. A domain whose last
    // two labels aren't in this set is assumed to need only the last two labels kept (correct for
    // ordinary "example.com"-shaped domains, and harmless — merely a no-op — for one that's
    // already exactly two labels).
    private static readonly HashSet<string> TwoLabelPublicSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.jp", "ne.jp", "or.jp", "ac.jp", "go.jp", "ad.jp", "gr.jp", "ed.jp", "lg.jp",
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk",
        "com.au", "net.au", "org.au",
        "co.nz", "co.kr", "co.in",
    };

    private static string ToRegistrableDomain(string domain)
    {
        var labels = domain.Split('.');
        if (labels.Length <= 2)
            return domain;

        var lastTwo = string.Join('.', labels[^2..]);
        var labelsToKeep = TwoLabelPublicSuffixes.Contains(lastTwo) ? 3 : 2;
        return labels.Length <= labelsToKeep ? domain : string.Join('.', labels[^labelsToKeep..]);
    }

    private static string? ExtractDomain(string emailAddress)
    {
        var rawDomain = ExtractRawDomain(emailAddress);
        return rawDomain is null ? null : ToRegistrableDomain(rawDomain);
    }

    // Unlike ExtractDomain, doesn't collapse a subdomain down to its registrable domain — needed
    // by GetLogoPathAsync's BIMI lookup, which has to try the exact sending domain first (see the
    // comment on FetchAndCacheBimiAsync for why).
    private static string? ExtractRawDomain(string emailAddress)
    {
        var at = emailAddress.LastIndexOf('@');
        if (at < 0 || at == emailAddress.Length - 1)
            return null;

        var domain = emailAddress[(at + 1)..].Trim().ToLowerInvariant();
        return domain.Length == 0 || !domain.All(c => !Path.GetInvalidFileNameChars().Contains(c))
            ? null
            : domain;
    }
}
