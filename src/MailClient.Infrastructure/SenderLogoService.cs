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
        var path = Path.Combine(appDataPaths.LogosDirectory, $"{domain}.png");
        if (File.Exists(path))
            return path;

        return await _inFlight.GetOrAdd(domain, _ => FetchAndCacheAsync(domain, path, ct));
    }

    public bool IsLogoCached(string emailAddress)
    {
        var domain = ExtractDomain(emailAddress);
        if (domain is null)
            return false;

        return File.Exists(Path.Combine(appDataPaths.LogosDirectory, $"{domain}.png"));
    }

    private async Task<string?> FetchAndCacheAsync(string domain, string path, CancellationToken ct)
    {
        try
        {
            // Google's public favicon endpoint: simple, free, no API key. Falls back to a
            // generic globe icon for domains it doesn't recognize rather than erroring, so this
            // won't always distinguish "no logo" from "unrecognized domain" — acceptable given
            // the caller always has the colored-initial avatar as a visual fallback anyway.
            var url = $"https://www.google.com/s2/favicons?sz=64&domain={Uri.EscapeDataString(domain)}";
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

    private static string? ExtractDomain(string emailAddress)
    {
        var at = emailAddress.LastIndexOf('@');
        if (at < 0 || at == emailAddress.Length - 1)
            return null;

        var domain = emailAddress[(at + 1)..].Trim().ToLowerInvariant();
        return domain.Length > 0 && domain.All(c => !Path.GetInvalidFileNameChars().Contains(c))
            ? domain
            : null;
    }
}
