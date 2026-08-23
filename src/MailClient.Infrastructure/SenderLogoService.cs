using System.Collections.Concurrent;
using MailClient.Core;
using MailClient.Core.Abstractions;

namespace MailClient.Infrastructure;

public sealed class SenderLogoService(AppDataPaths appDataPaths, ISettingsStore settingsStore) : ISenderLogoService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    // Coalesces concurrent requests for the same domain — many message rows share a sender,
    // and without this each one would kick off its own redundant download.
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new();

    public async Task<string?> GetLogoPathAsync(string emailAddress, CancellationToken ct)
    {
        if (!await settingsStore.GetShowSenderLogosEnabledAsync(ct))
            return null;

        var domain = ExtractDomain(emailAddress);
        if (domain is null)
            return null;

        Directory.CreateDirectory(appDataPaths.LogosDirectory);
        var path = CachePath(domain);
        if (File.Exists(path))
            return path;

        return await _inFlight.GetOrAdd(domain, _ => FetchAndCacheAsync(domain, path, ct));
    }

    public bool IsLogoCached(string emailAddress) => GetCachedLogoPath(emailAddress) is not null;

    public string? GetCachedLogoPath(string emailAddress)
    {
        var domain = ExtractDomain(emailAddress);
        if (domain is null)
            return null;

        var path = CachePath(domain);
        return File.Exists(path) ? path : null;
    }

    // The cache filename bakes in the requested size so that bumping RequestedSize automatically
    // invalidates every previously-cached (lower-resolution) logo instead of silently continuing
    // to serve it forever — a plain "{domain}.png" name would never expire.
    private const int RequestedSize = 256;
    private const string SizeSuffix = "-256";

    private string CachePath(string domain) => Path.Combine(appDataPaths.LogosDirectory, $"{domain}{SizeSuffix}.png");

    private async Task<string?> FetchAndCacheAsync(string domain, string path, CancellationToken ct)
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
    // serve. Reducing to the registrable domain before querying turns those into logo hits
    // instead of falling back to the colored-initial avatar.
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
        var at = emailAddress.LastIndexOf('@');
        if (at < 0 || at == emailAddress.Length - 1)
            return null;

        var domain = emailAddress[(at + 1)..].Trim().ToLowerInvariant();
        if (domain.Length == 0 || !domain.All(c => !Path.GetInvalidFileNameChars().Contains(c)))
            return null;

        return ToRegistrableDomain(domain);
    }
}
