using MailClient.App.Views.Rules;
using MailClient.Core.Models;
using MailClient.ViewModels.Rules;
using MailClient.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailClient.App.Views.Settings;

public sealed partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Account account })
            return;

        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "アカウントを削除しますか？",
            Content = $"{account.DisplayName} ({account.EmailAddress}) をこのアプリから削除します。ローカルに保存されたメールも削除されます。",
            PrimaryButtonText = "完全に削除",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteAccountCommand.ExecuteAsync(account);
    }

    private void OnManageRulesClick(object sender, RoutedEventArgs e)
    {
        var rulesViewModel = App.Services.GetRequiredService<RulesViewModel>();
        var rulesWindow = new RulesWindow(rulesViewModel);
        rulesWindow.Activate();
    }
}
