using MailClient.App.Views.AccountSetup;
using MailClient.Core.Models;
using MailClient.ViewModels.AccountSetup;
using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailClient.App.Views.Shell;

public sealed partial class SidebarView : UserControl
{
    public SidebarViewModel ViewModel { get; }

    public SidebarView()
    {
        ViewModel = App.Services.GetRequiredService<SidebarViewModel>();
        InitializeComponent();

        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        var dialogViewModel = App.Services.GetRequiredService<AddAccountViewModel>();
        var dialog = new AddAccountDialog(dialogViewModel)
        {
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnFolderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is MailFolder folder)
            ViewModel.SelectFolderCommand.Execute(folder);
    }
}
