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
}
