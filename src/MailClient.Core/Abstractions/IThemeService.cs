using MailClient.Core.Models;

namespace MailClient.Core.Abstractions;

// Owns the app's light/dark choice: persists it and applies it to every open window. The
// implementation lives in the UI project (it has to touch windows); view models only see this.
public interface IThemeService
{
    ThemeMode Mode { get; }

    // Persists the choice and applies it to all windows immediately.
    Task SetModeAsync(ThemeMode mode, CancellationToken ct);

    event EventHandler? ModeChanged;
}
