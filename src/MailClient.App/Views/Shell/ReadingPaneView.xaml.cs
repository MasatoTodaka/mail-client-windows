using MailClient.App.Views.Compose;
using MailClient.ViewModels.Compose;
using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MailClient.App.Views.Shell;

public sealed partial class ReadingPaneView : UserControl
{
    public ReadingPaneViewModel ViewModel { get; }

    public ReadingPaneView()
    {
        ViewModel = App.Services.GetRequiredService<ReadingPaneViewModel>();
        InitializeComponent();

        ViewModel.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(ReadingPaneViewModel.HtmlBody) && ViewModel.HtmlBody is not null)
                await NavigateAsync(ViewModel.HtmlBody);
        };
    }

    private async Task NavigateAsync(string html)
    {
        await BodyWebView.EnsureCoreWebView2Async();
        BodyWebView.CoreWebView2.NavigateToString(html);
    }

    private Visibility IsPlainTextVisible(bool isHtml) => isHtml ? Visibility.Collapsed : Visibility.Visible;

    private static readonly Color[] AvatarPalette =
    [
        Color.FromArgb(255, 0x5A, 0x8D, 0xEE), // blue
        Color.FromArgb(255, 0x5A, 0xC8, 0xA8), // teal
        Color.FromArgb(255, 0xE8, 0x8A, 0x4C), // orange
        Color.FromArgb(255, 0xC0, 0x6C, 0xE0), // purple
        Color.FromArgb(255, 0xE0, 0x6C, 0x8A), // pink
        Color.FromArgb(255, 0x6B, 0xB8, 0x5E), // green
    ];

    private string SenderInitial(string? fromDisplay) =>
        string.IsNullOrWhiteSpace(fromDisplay) ? "?" : fromDisplay.Trim()[..1].ToUpperInvariant();

    private Brush SenderAvatarColor(string? fromDisplay)
    {
        var text = string.IsNullOrWhiteSpace(fromDisplay) ? "?" : fromDisplay;
        var sum = 0;
        foreach (var c in text)
            sum += c;

        return new SolidColorBrush(AvatarPalette[sum % AvatarPalette.Length]);
    }

    private void OnReplyClick(object sender, RoutedEventArgs e) => OpenCompose(ComposeMode.Reply);

    private void OnReplyAllClick(object sender, RoutedEventArgs e) => OpenCompose(ComposeMode.ReplyAll);

    private void OnForwardClick(object sender, RoutedEventArgs e) => OpenCompose(ComposeMode.Forward);

    private void OpenCompose(ComposeMode mode)
    {
        var sourceMessage = ViewModel.SelectedMessage;
        if (sourceMessage is null)
            return;

        var quotedBody = ViewModel.IsHtml ? null : ViewModel.PlainTextBody;

        var composeViewModel = App.Services.GetRequiredService<ComposeViewModel>();
        composeViewModel.Prepare(mode, sourceMessage, quotedBody);

        var composeWindow = new ComposeWindow(composeViewModel);
        composeWindow.Activate();
    }
}
