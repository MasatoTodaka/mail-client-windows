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
        Color.FromArgb(255, 0x5A, 0x8D, 0xEE), // blue
        Color.FromArgb(255, 0x5A, 0xC8, 0xA8), // teal
        Color.FromArgb(255, 0xE8, 0x8A, 0x4C), // orange
        Color.FromArgb(255, 0xC0, 0x6C, 0xE0), // purple
        Color.FromArgb(255, 0xE0, 0x6C, 0x8A), // pink
        Color.FromArgb(255, 0x6B, 0xB8, 0x5E), // green
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
