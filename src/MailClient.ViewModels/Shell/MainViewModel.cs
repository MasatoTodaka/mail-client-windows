using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// Root shell view model. Sub-panes get their own view models (SidebarViewModel,
// MessageListViewModel, ReadingPaneViewModel) wired in as real data arrives in later milestones.
public sealed partial class MainViewModel(IAccountStore accountStore) : ViewModelBase
{
    public string SidebarPlaceholderText => "アカウント / フォルダ (M3で実装)";
    public string MessageListPlaceholderText => "メッセージ一覧 (M4で実装)";
    public string ReadingPanePlaceholderText => "メッセージ本文 (M5で実装)";

    // M2 dev-verification list: confirms accounts saved via AddAccountDialog survive a restart.
    // Superseded by the real account/folder tree in M3.
    public ObservableCollection<Account> Accounts { get; } = [];

    [RelayCommand]
    public async Task LoadAccountsAsync()
    {
        var accounts = await accountStore.GetAllAsync(CancellationToken.None);
        Accounts.Clear();
        foreach (var account in accounts)
            Accounts.Add(account);
    }
}
