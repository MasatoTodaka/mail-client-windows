namespace MailClient.Core.Models;

// Which color theme the app uses. Stored as a stable string (see ThemeModeExtensions) rather than
// the enum's number, so reordering or adding a member can never silently change a saved choice.
public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public static class ThemeModeExtensions
{
    public static string ToStorageValue(this ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        _ => "system",
    };

    // Anything missing or unrecognized (including a value written by a future version) means "follow the system".
    public static ThemeMode ParseThemeMode(string? value) => value switch
    {
        "light" => ThemeMode.Light,
        "dark" => ThemeMode.Dark,
        _ => ThemeMode.System,
    };
}
