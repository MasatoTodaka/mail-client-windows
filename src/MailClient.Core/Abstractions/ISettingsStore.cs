namespace MailClient.Core.Abstractions;

// App-level (not per-account) user preferences. Backed by a generic key/value table so new
// settings don't need a schema migration each time.
public interface ISettingsStore
{
    Task<bool> GetNotificationsEnabledAsync(CancellationToken ct);
    Task SetNotificationsEnabledAsync(bool enabled, CancellationToken ct);
}
