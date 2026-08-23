using MailClient.Core.Abstractions;
using MailClient.Core.Events;
using MailClient.Core.Models;

namespace MailClient.ViewModels.Tests;

// Minimal stand-ins for MessageListViewModel's constructor dependencies. The tests in this
// project drive ApplyMessages directly via reflection (it's private, and there's no
// InternalsVisibleTo — see MessageListViewModelApplyMessagesTests for why that's the right call),
// so none of these fakes' methods are actually invoked; they only need to exist and compile so a
// MessageListViewModel instance can be constructed.
internal sealed class FakeMessageStore : IMessageStore
{
    public Task<IReadOnlyList<MailMessage>> GetPageAsync(Guid folderId, int skip, int take, CancellationToken ct) => throw new NotImplementedException();
    public Task<MailMessage?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
    public Task<MailMessage?> GetByUidAsync(Guid folderId, uint uid, CancellationToken ct) => throw new NotImplementedException();
    public Task<uint?> GetMinUidAsync(Guid folderId, CancellationToken ct) => throw new NotImplementedException();
    public Task<(int Total, int Unread)> GetFolderCountsAsync(Guid folderId, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<MailMessage>> GetFlaggedPageAsync(Guid accountId, int skip, int take, CancellationToken ct) => throw new NotImplementedException();
    public Task<int> GetFlaggedCountAsync(Guid accountId, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<MailMessage>> GetTodayPageAsync(Guid accountId, int skip, int take, CancellationToken ct) => throw new NotImplementedException();
    public Task<int> GetTodayCountAsync(Guid accountId, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<MailMessage>> GetByAccountAsync(Guid accountId, CancellationToken ct) => throw new NotImplementedException();
    public Task<int> FixMojibakeSubjectsAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<MailMessage>> GetDownloadedBodyMessagesAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task SaveAsync(MailMessage message, CancellationToken ct) => throw new NotImplementedException();
    public Task UpdateDateAsync(Guid messageId, DateTimeOffset date, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
    public Task SetReadAsync(Guid id, bool isRead, CancellationToken ct) => throw new NotImplementedException();
    public Task SetFlaggedAsync(Guid id, bool isFlagged, CancellationToken ct) => throw new NotImplementedException();
}

internal sealed class FakeFolderStore : IFolderStore
{
    public Task<IReadOnlyList<MailFolder>> GetByAccountAsync(Guid accountId, CancellationToken ct) => throw new NotImplementedException();
    public Task<MailFolder?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
    public Task SaveAsync(MailFolder folder, CancellationToken ct) => throw new NotImplementedException();
    public Task UpdateCountsAsync(Guid folderId, int unreadCount, int totalCount, CancellationToken ct) => throw new NotImplementedException();
    public Task ReorderAsync(IReadOnlyList<Guid> orderedFolderIds, CancellationToken ct) => throw new NotImplementedException();
}

internal sealed class FakeMailSyncService : IMailSyncService
{
    public Task InitialSyncAsync(Guid accountId, CancellationToken ct) => Task.CompletedTask;
    public Task SyncFolderAsync(Guid folderId, SyncDepth depth, CancellationToken ct) => Task.CompletedTask;
    public Task SyncAllFolderCountsAsync(Guid accountId, CancellationToken ct) => Task.CompletedTask;
    public Task BackfillReceivedDatesAsync(Guid accountId, CancellationToken ct) => Task.CompletedTask;
    public Task RefreshFlaggedFolderCountAsync(Guid accountId, CancellationToken ct) => Task.CompletedTask;
    public Task RefreshTodayFolderCountAsync(Guid accountId, CancellationToken ct) => Task.CompletedTask;
    public Task StartLiveUpdatesAsync(Guid accountId, CancellationToken ct) => Task.CompletedTask;
    public Task StopLiveUpdatesAsync(Guid accountId) => Task.CompletedTask;

    public event EventHandler<MessageArrivedEventArgs>? MessageArrived;
    public event EventHandler<SyncProgressEventArgs>? SyncProgressChanged;
    public event EventHandler<FolderCountsChangedEventArgs>? FolderCountsChanged;
}

internal sealed class FakeUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class FakeOutboxStore : IOutboxStore
{
    public Task EnqueueAsync(OutboxAction action, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<OutboxAction>> GetPendingAsync(Guid accountId, CancellationToken ct) => throw new NotImplementedException();
    public Task MarkSucceededAsync(Guid actionId, CancellationToken ct) => throw new NotImplementedException();
    public Task MarkFailedAsync(Guid actionId, string error, CancellationToken ct) => throw new NotImplementedException();
}

internal sealed class FakeOutboxProcessor : IOutboxProcessor
{
    public Task ProcessAsync(Guid accountId, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class FakeSenderLogoService : ISenderLogoService
{
    public Task<string?> GetLogoPathAsync(string emailAddress, CancellationToken ct) => Task.FromResult<string?>(null);

    // Always "cached" so PrefetchLogosAsync (fired unawaited from LoadAsync/RefreshCurrentAsync)
    // sees nothing to fetch and returns immediately — these tests care about ApplyMessages'
    // synchronous diffing, not the logo-prefetch side effect.
    public bool IsLogoCached(string emailAddress) => true;
    public string? GetCachedLogoPath(string emailAddress) => null;
}
