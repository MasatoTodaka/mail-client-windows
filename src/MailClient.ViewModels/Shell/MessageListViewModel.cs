using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// M4: loads (and, on first view, synchronizes) the selected folder's most recent headers.
// Body fetch on selection lands in M5; infinite-scroll paging lands later.
public sealed partial class MessageListViewModel(IMessageStore messageStore, IMailSyncService syncService) : ViewModelBase
{
    private const int PageSize = 200;

    public ObservableCollection<MailMessage> Messages { get; } = [];

    [ObservableProperty]
    private string? _folderDisplayName;

    public async Task LoadAsync(MailFolder folder)
    {
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
}
