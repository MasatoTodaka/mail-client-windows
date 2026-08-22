using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Settings;

// M11: account management (delete) and app-level preferences, reached from the sidebar's gear button.
public sealed partial class SettingsViewModel(
    IAccountStore accountStore,
    ICredentialStore credentialStore,
    IMailSyncService mailSyncService,
    ISettingsStore settingsStore) : ViewModelBase
{
    private bool _isLoadingNotificationsEnabled;

    public ObservableCollection<Account> Accounts { get; } = [];

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    // Raised after an account is deleted, so the sidebar can drop it without a full window reopen.
    public event EventHandler<Guid>? AccountDeleted;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var accounts = await accountStore.GetAllAsync(CancellationToken.None);
        Accounts.Clear();
        foreach (var account in accounts)
            Accounts.Add(account);

        _isLoadingNotificationsEnabled = true;
        NotificationsEnabled = await settingsStore.GetNotificationsEnabledAsync(CancellationToken.None);
        _isLoadingNotificationsEnabled = false;
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(Account account)
    {
        await mailSyncService.StopLiveUpdatesAsync(account.Id);
        await accountStore.DeleteAsync(account.Id, CancellationToken.None);
        credentialStore.Remove(account.Id);
        Accounts.Remove(account);
        AccountDeleted?.Invoke(this, account.Id);
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        if (_isLoadingNotificationsEnabled)
            return; // don't write back the value we just loaded

        _ = settingsStore.SetNotificationsEnabledAsync(value, CancellationToken.None);
    }
}
