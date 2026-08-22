using MailClient.Core.Abstractions;

namespace MailClient.Data.Repositories;

public sealed class SettingsRepository(MailDbContext db) : ISettingsStore
{
    private const string NotificationsEnabledKey = "notifications_enabled";

    public async Task<bool> GetNotificationsEnabledAsync(CancellationToken ct)
    {
        var value = await GetAsync(NotificationsEnabledKey, ct);
        return value is null || value == "1"; // default: enabled
    }

    public Task SetNotificationsEnabledAsync(bool enabled, CancellationToken ct) =>
        SetAsync(NotificationsEnabledKey, enabled ? "1" : "0", ct);

    private async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        var result = await command.ExecuteScalarAsync(ct);
        return result as string;
    }

    private async Task SetAsync(string key, string value, CancellationToken ct)
    {
        await using var connection = db.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(ct);
    }
}
