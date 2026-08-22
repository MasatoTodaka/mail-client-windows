using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// M4: loads (and, on first view, synchronizes) the selected folder's most recent headers.
// M5: reports which message the user opened, and can be told to refresh (e.g. after the
// reading pane marks a message read) so the list's unread styling stays in sync.
public sealed partial class MessageListViewModel(IMessageStore messageStore, IMailSyncService syncService) : ViewModelBase
{
    private const int PageSize = 200;

    private MailFolder? _currentFolder;

    public ObservableCollection<MailMessage> Messages { get; } = [];

    [ObservableProperty]
    private string? _folderDisplayName;

    public event EventHandler<MailMessage>? MessageSelected;

    [RelayCommand]
    private void SelectMessage(MailMessage message) => MessageSelected?.Invoke(this, message);

    public async Task LoadAsync(MailFolder folder)
    {
        _currentFolder = folder;
        FolderDisplayName = folder.DisplayName;
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            if (folder.LastSyncedAt is null)
                await syncService.SyncFolderAsync(folder.Id, SyncDepth.RecentOnly, CancellationToken.None);

            var messages = await messageStore.GetPageAsync(folder.Id, skip: 0, take: PageSize, CancellationToken.None);
            Messages.Clear();
            foreach (var message in messages)
                Messages.Add(message);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"メッセージの取得に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Re-reads the current folder's messages from local storage (no re-sync) so read/flag
    // state changes made elsewhere (e.g. the reading pane) show up in the list.
    public async Task RefreshCurrentAsync()
    {
        if (_currentFolder is null)
            return;

        var messages = await messageStore.GetPageAsync(_currentFolder.Id, skip: 0, take: PageSize, CancellationToken.None);
        Messages.Clear();
        foreach (var message in messages)
            Messages.Add(message);
    }
}
