using MailClient.App.Views.AccountSetup;
using MailClient.ViewModels.AccountSetup;
using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MailClient.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();

        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();

        _ = ViewModel.LoadAccountsAsync();
    }

    private async void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        var dialogViewModel = App.Services.GetRequiredService<AddAccountViewModel>();
        var dialog = new AddAccountDialog(dialogViewModel)
        {
            XamlRoot = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await ViewModel.LoadAccountsAsync();
    }
}
