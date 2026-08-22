using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core.Abstractions;
using MailClient.Core.Events;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// M4: loads (and, on first view, synchronizes) the selected folder's most recent headers.
// M5: reports which message the user opened, and can be told to refresh (e.g. after the
// reading pane marks a message read) so the list's unread styling stays in sync.
// M8: also refreshes automatically when IMAP IDLE/polling reports a new message for whichever
// folder is currently displayed.
public sealed partial class MessageListViewModel : ViewModelBase
{
    private const int PageSize = 200;

    private readonly IMessageStore _messageStore;
    private readonly IMailSyncService _syncService;
    private readonly IUiDispatcher _uiDispatcher;
    private MailFolder? _currentFolder;

    public MessageListViewModel(IMessageStore messageStore, IMailSyncService syncService, IUiDispatcher uiDispatcher)
    {
        _messageStore = messageStore;
        _syncService = syncService;
        _uiDispatcher = uiDispatcher;
        _syncService.MessageArrived += OnMessageArrived;
    }

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
                await _syncService.SyncFolderAsync(folder.Id, SyncDepth.RecentOnly, CancellationToken.None);

            var messages = await _messageStore.GetPageAsync(folder.Id, skip: 0, take: PageSize, CancellationToken.None);
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

    // Pages further back into the folder's history (SyncDepth.ExtendBackward), then reloads the
    // list from local storage so the newly-fetched older messages appear alongside the rest.
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_currentFolder is null)
            return;

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await _syncService.SyncFolderAsync(_currentFolder.Id, SyncDepth.ExtendBackward, CancellationToken.None);
            await RefreshCurrentAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"読み込みに失敗しました: {ex.Message}";
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

        var messages = await _messageStore.GetPageAsync(_currentFolder.Id, skip: 0, take: PageSize, CancellationToken.None);
        Messages.Clear();
        foreach (var message in messages)
            Messages.Add(message);
    }

    // May fire from the IMAP IDLE watcher's background thread, so everything here — including
    // reading _currentFolder — must run after the hop back onto the UI thread.
    private void OnMessageArrived(object? sender, MessageArrivedEventArgs e)
    {
        _uiDispatcher.Post(() =>
        {
            if (_currentFolder is not null && e.Message.FolderId == _currentFolder.Id)
                _ = RefreshCurrentAsync();
        });
    }
}
