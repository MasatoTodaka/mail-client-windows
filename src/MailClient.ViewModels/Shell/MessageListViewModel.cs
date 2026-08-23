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
    private readonly IFolderStore _folderStore;
    private readonly IMailSyncService _syncService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly MessageActionService _messageActions;
    private MailFolder? _currentFolder;

    public MessageListViewModel(
        IMessageStore messageStore,
        IFolderStore folderStore,
        IMailSyncService syncService,
        IUiDispatcher uiDispatcher,
        MessageActionService messageActions)
    {
        _messageStore = messageStore;
        _folderStore = folderStore;
        _syncService = syncService;
        _uiDispatcher = uiDispatcher;
        _messageActions = messageActions;
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
            // "フラグ付き"/"今日" are virtual aggregates (no ImapFullName) — there's nothing to
            // sync from IMAP directly; they just reflect locally-known messages.
            if (!IsVirtualFolder(folder) && folder.LastSyncedAt is null)
                await _syncService.SyncFolderAsync(folder.Id, SyncDepth.RecentOnly, CancellationToken.None);

            var messages = await LoadPageAsync(folder);
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
            if (!IsVirtualFolder(_currentFolder))
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

        var messages = await LoadPageAsync(_currentFolder);
        Messages.Clear();
        foreach (var message in messages)
            Messages.Add(message);
    }

    private Task<IReadOnlyList<MailMessage>> LoadPageAsync(MailFolder folder) => folder.SpecialUse switch
    {
        MailFolderSpecialUse.Flagged => _messageStore.GetFlaggedPageAsync(folder.AccountId, skip: 0, take: PageSize, CancellationToken.None),
        MailFolderSpecialUse.Today => _messageStore.GetTodayPageAsync(folder.AccountId, skip: 0, take: PageSize, CancellationToken.None),
        _ => _messageStore.GetPageAsync(folder.Id, skip: 0, take: PageSize, CancellationToken.None),
    };

    private static bool IsVirtualFolder(MailFolder folder) =>
        folder.SpecialUse is MailFolderSpecialUse.Flagged or MailFolderSpecialUse.Today;

    // Available move targets for the right-click "移動" submenu: every real folder in the
    // account except the message's own current folder (can't move it into itself) and any
    // virtual folder (nothing to actually move into — not a real IMAP folder).
    // Takes the message's folder explicitly (not _currentFolder) since when viewing a virtual
    // aggregate folder, _currentFolder is the virtual folder itself, not where the message
    // actually lives.
    public async Task<IReadOnlyList<MailFolder>> GetMoveTargetFoldersAsync(Guid accountId, Guid excludeFolderId)
    {
        var folders = await _folderStore.GetByAccountAsync(accountId, CancellationToken.None);
        return folders
            .Where(f => f.ImapFullName is not null && f.Id != excludeFolderId)
            .OrderBy(f => f.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    public async Task ToggleFlagOnMessageAsync(MailMessage message)
    {
        await _messageActions.ToggleFlagAsync(message, CancellationToken.None);
        await RefreshCurrentAsync();
    }

    public async Task ToggleReadOnMessageAsync(MailMessage message)
    {
        await _messageActions.SetReadAsync(message, !message.IsRead, CancellationToken.None);
        await RefreshCurrentAsync();
    }

    public async Task DeleteMessageAsync(MailMessage message)
    {
        ErrorMessage = await _messageActions.MoveToSpecialFolderAsync(message, MailFolderSpecialUse.Trash, CancellationToken.None);
        await RefreshCurrentAsync();
    }

    public async Task ArchiveMessageAsync(MailMessage message)
    {
        ErrorMessage = await _messageActions.MoveToSpecialFolderAsync(message, MailFolderSpecialUse.Archive, CancellationToken.None);
        await RefreshCurrentAsync();
    }

    public async Task MoveMessageAsync(MailMessage message, MailFolder target)
    {
        ErrorMessage = await _messageActions.MoveToFolderAsync(message, target, CancellationToken.None);
        await RefreshCurrentAsync();
    }

    // May fire from the IMAP IDLE watcher's background thread, so everything here — including
    // reading _currentFolder — must run after the hop back onto the UI thread.
    private void OnMessageArrived(object? sender, MessageArrivedEventArgs e)
    {
        _uiDispatcher.Post(() =>
        {
            if (_currentFolder is null)
                return;

            // New mail always belongs to "today", so the virtual "今日" view needs a refresh
            // regardless of which real folder the message landed in.
            if (e.Message.FolderId == _currentFolder.Id || _currentFolder.SpecialUse == MailFolderSpecialUse.Today)
                _ = RefreshCurrentAsync();
        });
    }
}
