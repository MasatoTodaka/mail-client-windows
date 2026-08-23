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
    private readonly ISenderLogoService _senderLogoService;
    private MailFolder? _currentFolder;

    // Snapshot of each row's displayed fields as of the last ApplyMessages pass, keyed by message
    // Id — see the comment on ApplyMessages for why this can't just compare against the live
    // MailMessage objects sitting in Messages.
    private readonly Dictionary<Guid, DisplayState> _lastAppliedState = [];

    public MessageListViewModel(
        IMessageStore messageStore,
        IFolderStore folderStore,
        IMailSyncService syncService,
        IUiDispatcher uiDispatcher,
        MessageActionService messageActions,
        ISenderLogoService senderLogoService)
    {
        _messageStore = messageStore;
        _folderStore = folderStore;
        _syncService = syncService;
        _uiDispatcher = uiDispatcher;
        _messageActions = messageActions;
        _senderLogoService = senderLogoService;
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
            ApplyMessages(messages);
            _ = PrefetchLogosAsync(messages);
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
        ApplyMessages(messages);
        _ = PrefetchLogosAsync(messages);
    }

    // In-place diff/reconcile rather than Clear()+Add() — the ListView visually collapses to
    // empty and rebuilds on Clear() (losing scroll position and producing the "jumps like a
    // pull-to-refresh" motion users noticed after every read/flag/logo-driven refresh). Ending
    // up at the same final list via targeted Replace/Move/Insert/Remove keeps everything else
    // in the ListView undisturbed.
    //
    // Every call re-queries the store, so even an unchanged row arrives as a brand new MailMessage
    // instance — replacing the collection slot unconditionally would still touch all ~200 rows on
    // every refresh (each Replace nudges the ListView's item-reveal transition), reproducing the
    // same jump this diff is meant to avoid. Only rows whose displayed fields actually changed
    // since the last pass (per _lastAppliedState) get replaced.
    //
    // Deliberately NOT compared against the live MailMessage sitting in Messages[i]: MessageActionService
    // mutates that exact shared instance in place (e.g. message.IsRead = true) before this runs, so
    // by the time ApplyMessages sees it, the "old" and "new" objects already agree — comparing
    // against them would silently swallow the very change we're here to pick up (this is what
    // broke "既読にする" after the previous fix). _lastAppliedState is a separate snapshot that
    // only this method ever writes, so it still reflects what was actually shown last time.
    //
    // IsRead/IsFlagged are excluded from that snapshot and handled separately (just below): they're
    // the two fields MessageActionService mutates in place and MailMessage now raises
    // PropertyChanged for, and MessageListView binds Mode=OneWay, so propagating them onto the
    // existing row's setter is enough to update just the unread dot / flag icon — no need to
    // replace the row (which would otherwise recreate its container and disturb its selection
    // highlight) just because the user marked it read or flagged it.
    private void ApplyMessages(IReadOnlyList<MailMessage> messages)
    {
        var newIds = messages.Select(m => m.Id).ToHashSet();
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (!newIds.Contains(Messages[i].Id))
            {
                _lastAppliedState.Remove(Messages[i].Id);
                Messages.RemoveAt(i);
            }
        }

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];

            if (i < Messages.Count && Messages[i].Id == message.Id)
            {
                var existing = Messages[i];
                existing.IsRead = message.IsRead;
                existing.IsFlagged = message.IsFlagged;

                var newState = DisplayState.From(message);
                if (!_lastAppliedState.TryGetValue(message.Id, out var previousState) || previousState != newState)
                {
                    Messages[i] = message;
                    _lastAppliedState[message.Id] = newState;
                }
                continue;
            }

            var existingIndex = -1;
            for (var j = i; j < Messages.Count; j++)
            {
                if (Messages[j].Id == message.Id)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
                Messages.Move(existingIndex, i);
            else
                Messages.Insert(i, message);

            _lastAppliedState[message.Id] = DisplayState.From(message);
        }
    }

    // Plain-value snapshot of the message list's DataTemplate-bound fields that are still
    // effectively OneTime (i.e. everything except IsRead/IsFlagged, which are live-notified —
    // see ApplyMessages above), used solely to detect real changes between ApplyMessages passes.
    private readonly record struct DisplayState(
        uint Uid,
        Guid FolderId,
        string Subject,
        string FromDisplay,
        string FromAddress,
        DateTimeOffset Date,
        string Snippet,
        bool IsAnswered,
        bool IsDraft,
        bool HasAttachments,
        bool IsBodyDownloaded)
    {
        public static DisplayState From(MailMessage m) => new(
            m.Uid, m.FolderId, m.Subject, m.FromDisplay, m.FromAddress, m.Date, m.Snippet,
            m.IsAnswered, m.IsDraft, m.HasAttachments, m.IsBodyDownloaded);
    }

    // Fire-and-forget: makes sure every unique sender domain on the page has its logo cached
    // (a no-op per domain once already cached), then re-applies the page once so any logos that
    // were genuinely fetched just now show up immediately instead of waiting for some unrelated
    // refresh trigger. Calls ApplyMessages directly (not RefreshCurrentAsync) so this doesn't
    // recursively kick off another prefetch pass.
    private async Task PrefetchLogosAsync(IReadOnlyList<MailMessage> messages)
    {
        try
        {
            var addressesToFetch = messages
                .Select(m => m.FromAddress)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct()
                .Where(a => !_senderLogoService.IsLogoCached(a))
                .ToList();

            if (addressesToFetch.Count == 0)
                return;

            var fetchedAny = false;
            foreach (var address in addressesToFetch)
            {
                if (await _senderLogoService.GetLogoPathAsync(address, CancellationToken.None) is not null)
                    fetchedAny = true;
            }

            if (fetchedAny && _currentFolder is not null)
                ApplyMessages(await LoadPageAsync(_currentFolder));
        }
        catch
        {
            // Best-effort: a logo prefetch failure must never disturb the message list itself.
        }
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
