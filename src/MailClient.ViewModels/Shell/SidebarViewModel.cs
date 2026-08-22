using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// M3: connects on demand and lists real folders. M4: kicks off INBOX header sync once folders
// are known, and reports which folder the user selected so MessageListViewModel can load it.
// M6: a successful connect is also the natural "we're back online" signal, so it drains any
// actions queued in the Outbox while this account was unreachable.
public sealed partial class SidebarViewModel(
    IAccountStore accountStore,
    IFolderStore folderStore,
    ICredentialStore credentialStore,
    IMailSyncService mailSyncService,
    IOutboxProcessor outboxProcessor,
    Func<IImapAccountClient> imapClientFactory) : ViewModelBase
{
    public ObservableCollection<AccountNode> Accounts { get; } = [];

    public event EventHandler<MailFolder>? FolderSelected;

    [RelayCommand]
    private void SelectFolder(MailFolder folder) => FolderSelected?.Invoke(this, folder);

    [RelayCommand]
    public async Task LoadAsync()
    {
        var accounts = await accountStore.GetAllAsync(CancellationToken.None);

        Accounts.Clear();
        foreach (var account in accounts)
            Accounts.Add(new AccountNode(account));

        foreach (var node in Accounts)
            _ = ConnectAndListFoldersAsync(node);
    }

    public async Task ConnectAndListFoldersAsync(AccountNode node)
    {
        node.ErrorMessage = null;
        node.IsConnecting = true;
        try
        {
            var password = credentialStore.GetImapPassword(node.Account.Id)
                ?? throw new ImapAuthenticationException("保存されたパスワードが見つかりません。アカウントを再作成してください。");

            var existingFolders = await folderStore.GetByAccountAsync(node.Account.Id, CancellationToken.None);
            var existingByFullName = existingFolders
                .Where(f => f.ImapFullName is not null)
                .ToDictionary(f => f.ImapFullName!);

            using var client = imapClientFactory();
            await client.ConnectAsync(node.Account, password, CancellationToken.None);
            var remoteFolders = await client.ListFoldersAsync(CancellationToken.None);
            await client.DisconnectAsync();

            node.Folders.Clear();
            foreach (var remoteFolder in remoteFolders)
            {
                var folderToSave = ReconcileWithExisting(remoteFolder, existingByFullName);
                await folderStore.SaveAsync(folderToSave, CancellationToken.None);
                node.Folders.Add(folderToSave);
            }

            // Fire-and-forget: warms up INBOX so it's ready by the time the user clicks it.
            // MessageListViewModel syncs on-demand too, so a race here just means a redundant sync.
            _ = mailSyncService.InitialSyncAsync(node.Account.Id, CancellationToken.None);

            // We just proved this account is reachable — drain anything queued while it wasn't.
            _ = outboxProcessor.ProcessAsync(node.Account.Id, CancellationToken.None);

            // Start (or no-op if already running) IDLE/polling so new INBOX mail shows up live.
            _ = mailSyncService.StartLiveUpdatesAsync(node.Account.Id, CancellationToken.None);
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
    private static MailFolder ReconcileWithExisting(MailFolder remote, Dictionary<string, MailFolder> existingByFullName)
    {
        if (remote.ImapFullName is null || !existingByFullName.TryGetValue(remote.ImapFullName, out var existing))
            return remote;

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
        };
    }

    private static string DescribeError(Exception ex) => ex switch
    {
        ImapAuthenticationException => "認証に失敗しました。ユーザー名またはパスワードを確認してください。",
        ImapConnectionException connEx => $"サーバーに接続できませんでした: {connEx.Message}",
        _ => $"接続エラー: {ex.Message}",
    };
}
