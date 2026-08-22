using MailClient.App.Views.AccountSetup;
using MailClient.App.Views.Settings;
using MailClient.Core.Models;
using MailClient.ViewModels.AccountSetup;
using MailClient.ViewModels.Settings;
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

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AccountNode node })
            ViewModel.RetryCommand.Execute(node);
    }

    // The ListView (bound to an ObservableCollection<MailFolder>) already reorders its own
    // ItemsSource in place when a drag completes — this just persists that new order.
    private void OnFolderDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (sender.DataContext is AccountNode node)
            ViewModel.ReorderFoldersCommand.Execute(node);
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settingsViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        var settingsWindow = new SettingsWindow(settingsViewModel);
        settingsViewModel.AccountDeleted += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
        settingsWindow.Activate();
    }

    private Visibility EmptyStateVisibility(int accountCount) =>
        accountCount == 0 ? Visibility.Visible : Visibility.Collapsed;
}
