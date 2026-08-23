using MailClient.App.Views.Compose;
using MailClient.App.Views.Converters;
using MailClient.ViewModels.Compose;
using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
            if (e.PropertyName == nameof(ReadingPaneViewModel.SelectedMessage))
                await UpdateSenderLogoAsync();
        };
    }

    private async Task UpdateSenderLogoAsync()
    {
        var message = ViewModel.SelectedMessage;
        if (message is null)
        {
            SenderLogoBrush.ImageSource = null;
            SenderLogoBacking.Background = null;
            return;
        }

        var path = await ViewModel.GetSenderLogoPathAsync(message.FromAddress);
        SenderLogoBrush.ImageSource = SenderLogoImageConverter.CreateImageSource(path);
        SenderLogoBacking.Background = path is null ? null : new SolidColorBrush(Colors.White);
    }

    private async Task NavigateAsync(string html)
    {
        await BodyWebView.EnsureCoreWebView2Async();
        BodyWebView.CoreWebView2.NavigateToString(html);
    }

    private Visibility IsPlainTextVisible(bool isHtml) => isHtml ? Visibility.Collapsed : Visibility.Visible;

    private string SenderInitial(string? fromDisplay) => SenderAvatar.Initial(fromDisplay);

    private Brush SenderAvatarColor(string? fromDisplay) => SenderAvatar.ColorFor(fromDisplay);

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
