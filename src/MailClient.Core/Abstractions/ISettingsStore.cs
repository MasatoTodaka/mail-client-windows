namespace MailClient.Core.Abstractions;

// App-level (not per-account) user preferences. Backed by a generic key/value table so new
// settings don't need a schema migration each time.
public interface ISettingsStore
{
    Task<bool> GetNotificationsEnabledAsync(CancellationToken ct);
    Task SetNotificationsEnabledAsync(bool enabled, CancellationToken ct);

    Task<bool> GetOtpAutoCopyEnabledAsync(CancellationToken ct);
    Task SetOtpAutoCopyEnabledAsync(bool enabled, CancellationToken ct);

    // Opt-in (default off): fetches each sender's domain favicon from an external service to
    // show as their avatar instead of the colored-initial fallback. Off by default since it
    // reveals sender domains to that third-party service, consistent with this app's default
    // of blocking remote content until the user opts in.
    Task<bool> GetShowSenderLogosEnabledAsync(CancellationToken ct);
    Task SetShowSenderLogosEnabledAsync(bool enabled, CancellationToken ct);

    // One-time-per-account guard for the received-date backfill (re-fetches IMAP INTERNALDATE
    // for already-cached messages so sort order matches actual arrival, not the sender's
    // self-reported Date: header). Keeps it from re-running on every launch once done.
    Task<bool> GetReceivedDateBackfillCompleteAsync(Guid accountId, CancellationToken ct);
    Task SetReceivedDateBackfillCompleteAsync(Guid accountId, CancellationToken ct);
}
