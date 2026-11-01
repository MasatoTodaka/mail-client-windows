using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MailClient.App.Views;

// Shared iOS Mail-style "colored circle + initial" sender avatar, used by both the reading pane
// header and the message list rows — same palette/hash so a given sender gets the same color in
// both places.
public static class SenderAvatar
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 0x2E, 0x8B, 0x7A), // teal
        Color.FromArgb(255, 0x7C, 0x5C, 0xD6), // violet
        Color.FromArgb(255, 0x0F, 0x6C, 0xBD), // blue
        Color.FromArgb(255, 0xC2, 0x41, 0x0C), // rust
        Color.FromArgb(255, 0xB4, 0x45, 0x7A), // rose
        Color.FromArgb(255, 0x4F, 0x7F, 0x3A), // moss
    ];

    public static string Initial(string? fromDisplay) =>
        string.IsNullOrWhiteSpace(fromDisplay) ? "?" : fromDisplay.Trim()[..1].ToUpperInvariant();

    public static Brush ColorFor(string? fromDisplay)
    {
        var text = string.IsNullOrWhiteSpace(fromDisplay) ? "?" : fromDisplay;
        var sum = 0;
        foreach (var c in text)
            sum += c;

        return new SolidColorBrush(Palette[sum % Palette.Length]);
    }
}
