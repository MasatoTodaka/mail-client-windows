namespace MailClient.Core.Abstractions;

// Opt-in (ISettingsStore.GetShowSenderLogosEnabledAsync) fetch-and-cache of a sender's domain
// logo/favicon from an external service, for display instead of the colored-initial avatar
// fallback.
public interface ISenderLogoService
{
    // Returns a local file path to the cached logo for the given sender email address, fetching
    // and caching it first if not already cached. Returns null if the setting is off, the
    // address has no usable domain, or the fetch failed — callers should fall back to the
    // colored-initial avatar in all of those cases.
    Task<string?> GetLogoPathAsync(string emailAddress, CancellationToken ct);

    // Synchronous, local-only check (no network) — lets a caller tell a genuine new fetch apart
    // from a cache hit before calling GetLogoPathAsync, without an async round trip.
    bool IsLogoCached(string emailAddress);

    // Synchronous, local-only (no network) — returns the cached logo's file path if one exists,
    // or null otherwise. Exists so callers that need the domain/filename logic (namely
    // SenderLogoImageConverter, which can't await anything from an x:Bind converter) don't have to
    // duplicate it themselves; duplicating it previously caused the converter's cache lookup to
    // silently go stale whenever the service's domain-matching logic changed underneath it.
    string? GetCachedLogoPath(string emailAddress);
}
