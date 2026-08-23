using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core.Abstractions;
using MailClient.Core.Events;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// M3: connects on demand and lists real folders. M4: kicks off INBOX header sync once folders
// are known, and reports which folder the user selected so MessageListViewModel can load it.
// M6: a successful connect is also the natural "we're back online" signal, so it drains any
// actions queued in the Outbox while this account was unreachable.
public sealed partial class SidebarViewModel : ViewModelBase
{
    private readonly IAccountStore _accountStore;
    private readonly IFolderStore _folderStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IMailSyncService _mailSyncService;
    private readonly IOutboxProcessor _outboxProcessor;
    private readonly Func<IImapAccountClient> _imapClientFactory;

    public SidebarViewModel(
        IAccountStore accountStore,
        IFolderStore folderStore,
        ICredentialStore credentialStore,
        IMailSyncService mailSyncService,
        IOutboxProcessor outboxProcessor,
        IUiDispatcher uiDispatcher,
        Func<IImapAccountClient> imapClientFactory)
    {
        _accountStore = accountStore;
        _folderStore = folderStore;
        _credentialStore = credentialStore;
        _mailSyncService = mailSyncService;
        _outboxProcessor = outboxProcessor;
        _imapClientFactory = imapClientFactory;

        // FolderCountsChanged can fire from the IMAP IDLE watcher's background thread (same
        // hazard as M10's MessageArrived), so this hop onto the UI thread before touching
        // AccountNode/Folders is required, not optional.
        mailSyncService.FolderCountsChanged += (_, e) => uiDispatcher.Post(() => OnFolderCountsChanged(e));
    }

    public ObservableCollection<AccountNode> Accounts { get; } = [];

    public event EventHandler<MailFolder>? FolderSelected;

    [RelayCommand]
    private void SelectFolder(MailFolder folder) => FolderSelected?.Invoke(this, folder);

    [RelayCommand]
    public async Task LoadAsync()
    {
        var accounts = await _accountStore.GetAllAsync(CancellationToken.None);

        Accounts.Clear();
        foreach (var account in accounts)
            Accounts.Add(new AccountNode(account));

        foreach (var node in Accounts)
            _ = ConnectAndListFoldersAsync(node);
    }

    [RelayCommand]
    private Task RetryAsync(AccountNode node) => ConnectAndListFoldersAsync(node);

    private void OnFolderCountsChanged(FolderCountsChangedEventArgs e)
    {
        foreach (var node in Accounts)
        {
            var index = node.Folders.ToList().FindIndex(f => f.Id == e.FolderId);
            if (index < 0)
                continue;

            var folder = node.Folders[index];
            node.Folders[index] = new MailFolder
            {
                Id = folder.Id,
                AccountId = folder.AccountId,
                ImapFullName = folder.ImapFullName,
                DisplayName = folder.DisplayName,
                SpecialUse = folder.SpecialUse,
                ParentFolderId = folder.ParentFolderId,
                UidValidity = folder.UidValidity,
                UidNext = folder.UidNext,
                HighestModSeq = folder.HighestModSeq,
                UnreadCount = e.UnreadCount,
                TotalCount = e.TotalCount,
                LastSyncedAt = folder.LastSyncedAt,
                SortOrder = folder.SortOrder,
            };
            node.RecalculateUnreadTotal();
            return;
        }
    }

    public async Task ConnectAndListFoldersAsync(AccountNode node)
    {
        node.ErrorMessage = null;
        node.IsConnecting = true;
        try
        {
            var password = _credentialStore.GetImapPassword(node.Account.Id)
                ?? throw new ImapAuthenticationException("保存されたパスワードが見つかりません。アカウントを再作成してください。");

            var existingFolders = await _folderStore.GetByAccountAsync(node.Account.Id, CancellationToken.None);
            var existingByFullName = existingFolders
                .Where(f => f.ImapFullName is not null)
                .ToDictionary(f => f.ImapFullName!);
            // Newly-discovered folders append after whatever the user has already arranged,
            // rather than defaulting to sort_order 0 and jumping to the front.
            var nextNewSortOrder = existingFolders.Count == 0 ? 0 : existingFolders.Max(f => f.SortOrder) + 1;

            using var client = _imapClientFactory();
            await client.ConnectAsync(node.Account, password, CancellationToken.None);
            var remoteFolders = await client.ListFoldersAsync(CancellationToken.None);
            await client.DisconnectAsync();

            var reconciled = new List<MailFolder>();
            foreach (var remoteFolder in remoteFolders)
            {
                var folderToSave = ReconcileWithExisting(remoteFolder, existingByFullName, ref nextNewSortOrder);
                await _folderStore.SaveAsync(folderToSave, CancellationToken.None);
                reconciled.Add(folderToSave);
            }

            // "フラグ付き" is a local-only virtual folder (no ImapFullName) that aggregates
            // flagged messages across every real folder — created once, then just re-included
            // on every reconnect so its Id/SortOrder (and any future drag-reorder) stick.
            var flaggedFolder = existingFolders.FirstOrDefault(f => f.SpecialUse == MailFolderSpecialUse.Flagged);
            if (flaggedFolder is null)
            {
                flaggedFolder = new MailFolder
                {
                    Id = Guid.NewGuid(),
                    AccountId = node.Account.Id,
                    DisplayName = "フラグ付き",
                    SpecialUse = MailFolderSpecialUse.Flagged,
                    SortOrder = -1,
                };
                await _folderStore.SaveAsync(flaggedFolder, CancellationToken.None);
            }
            reconciled.Add(flaggedFolder);

            // "今日" is the same kind of local-only virtual folder, aggregating messages
            // received (IMAP INTERNALDATE) since local midnight across every real folder.
            var todayFolder = existingFolders.FirstOrDefault(f => f.SpecialUse == MailFolderSpecialUse.Today);
            if (todayFolder is null)
            {
                todayFolder = new MailFolder
                {
                    Id = Guid.NewGuid(),
                    AccountId = node.Account.Id,
                    DisplayName = "今日",
                    SpecialUse = MailFolderSpecialUse.Today,
                    SortOrder = -2,
                };
                await _folderStore.SaveAsync(todayFolder, CancellationToken.None);
            }
            reconciled.Add(todayFolder);

            node.Folders.Clear();
            foreach (var folder in reconciled.OrderBy(f => f.SortOrder).ThenBy(f => f.DisplayName, StringComparer.Ordinal))
                node.Folders.Add(folder);
            node.RecalculateUnreadTotal();

            // Fire-and-forget: warms up INBOX so it's ready by the time the user clicks it.
            // MessageListViewModel syncs on-demand too, so a race here just means a redundant sync.
            _ = _mailSyncService.InitialSyncAsync(node.Account.Id, CancellationToken.None);

            // Fire-and-forget: badges every folder's unread/total count (lightweight IMAP
            // STATUS, no header fetch) so folders the user hasn't opened yet aren't stuck
            // showing stale/zero counts.
            _ = _mailSyncService.SyncAllFolderCountsAsync(node.Account.Id, CancellationToken.None);

            // Fire-and-forget: badges the virtual "フラグ付き" folder with the current local
            // flagged count (pure local query, no IMAP needed).
            _ = _mailSyncService.RefreshFlaggedFolderCountAsync(node.Account.Id, CancellationToken.None);

            // Fire-and-forget: badges the virtual "今日" folder (pure local query, no IMAP).
            _ = _mailSyncService.RefreshTodayFolderCountAsync(node.Account.Id, CancellationToken.None);

            // Fire-and-forget, one-time per account: corrects already-cached messages' sort
            // date to IMAP INTERNALDATE (actual received time) instead of the sender's Date:
            // header, which is what caused messages to occasionally sort out of order.
            _ = _mailSyncService.BackfillReceivedDatesAsync(node.Account.Id, CancellationToken.None);

            // We just proved this account is reachable — drain anything queued while it wasn't.
            _ = _outboxProcessor.ProcessAsync(node.Account.Id, CancellationToken.None);

            // Start (or no-op if already running) IDLE/polling so new INBOX mail shows up live.
            _ = _mailSyncService.StartLiveUpdatesAsync(node.Account.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            node.ErrorMessage = DescribeError(ex);
        }
        finally
        {
            node.IsConnecting = false;
        }
    }

    // Keeps the folder's local identity (and any sync-state already recorded for it) stable
    // across repeated ListFoldersAsync calls, so re-connecting doesn't duplicate rows.
    private static MailFolder ReconcileWithExisting(
        MailFolder remote, Dictionary<string, MailFolder> existingByFullName, ref int nextNewSortOrder)
    {
        if (remote.ImapFullName is null || !existingByFullName.TryGetValue(remote.ImapFullName, out var existing))
        {
            remote.SortOrder = nextNewSortOrder++;
            return remote;
        }

        return new MailFolder
        {
            Id = existing.Id,
            AccountId = remote.AccountId,
            ImapFullName = remote.ImapFullName,
            DisplayName = remote.DisplayName,
            SpecialUse = remote.SpecialUse,
            ParentFolderId = existing.ParentFolderId,
            UidValidity = existing.UidValidity,
            UidNext = existing.UidNext,
            HighestModSeq = existing.HighestModSeq,
            UnreadCount = existing.UnreadCount,
            TotalCount = existing.TotalCount,
            LastSyncedAt = existing.LastSyncedAt,
            SortOrder = existing.SortOrder,
        };
    }

    // Persists the sidebar's current on-screen folder order for this account (e.g. after a drag
    // reorder already mutated node.Folders in place).
    [RelayCommand]
    private Task ReorderFoldersAsync(AccountNode node) =>
        _folderStore.ReorderAsync(node.Folders.Select(f => f.Id).ToList(), CancellationToken.None);

    private static string DescribeError(Exception ex) => ex switch
    {
        ImapAuthenticationException => "認証に失敗しました。ユーザー名またはパスワードを確認してください。",
        ImapConnectionException connEx => $"サーバーに接続できませんでした: {connEx.Message}",
        _ => $"接続エラー: {ex.Message}",
    };
}
