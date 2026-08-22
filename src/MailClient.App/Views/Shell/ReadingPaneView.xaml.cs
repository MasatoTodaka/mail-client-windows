using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
}
