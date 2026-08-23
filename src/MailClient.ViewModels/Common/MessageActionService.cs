using System.Text.Json;
using MailClient.Core;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;

namespace MailClient.ViewModels.Common;

// Shared flag/read/move mutation logic used by both the reading pane's toolbar and the message
// list's right-click menu, so the two don't duplicate the Outbox-queueing pattern (M6: act on
// local state immediately, then queue the matching OutboxAction so it survives being offline and
// replays once connectivity returns).
public sealed class MessageActionService(
    IMessageStore messageStore,
    IFolderStore folderStore,
    IOutboxStore outboxStore,
    IOutboxProcessor outboxProcessor,
    IMailSyncService mailSyncService)
{
    public async Task ToggleFlagAsync(MailMessage message, CancellationToken ct)
    {
        var newFlagged = !message.IsFlagged;
        message.IsFlagged = newFlagged;
        await messageStore.SetFlaggedAsync(message.Id, newFlagged, ct);

        await EnqueueOutboxActionAsync(
            message.AccountId,
            newFlagged ? OutboxActionType.MarkFlagged : OutboxActionType.MarkUnflagged,
            message.Id,
            targetFolderId: null,
            new OutboxMessageRef(message.FolderId, message.Uid));

        await mailSyncService.RefreshFlaggedFolderCountAsync(message.AccountId, ct);
    }

    public async Task SetReadAsync(MailMessage message, bool isRead, CancellationToken ct)
    {
        if (message.IsRead == isRead)
            return;

        message.IsRead = isRead;
        await messageStore.SetReadAsync(message.Id, isRead, ct);

        var folder = await folderStore.GetByIdAsync(message.FolderId, ct);
        if (folder is not null)
        {
            var delta = isRead ? -1 : 1;
            var newUnread = Math.Max(0, folder.UnreadCount + delta);
            await folderStore.UpdateCountsAsync(folder.Id, newUnread, folder.TotalCount, ct);
        }

        await EnqueueOutboxActionAsync(
            message.AccountId,
            isRead ? OutboxActionType.MarkRead : OutboxActionType.MarkUnread,
            message.Id,
            targetFolderId: null,
            new OutboxMessageRef(message.FolderId, message.Uid));
    }

    public async Task<string?> MoveToSpecialFolderAsync(MailMessage message, MailFolderSpecialUse targetUse, CancellationToken ct)
    {
        var accountFolders = await folderStore.GetByAccountAsync(message.AccountId, ct);
        var target = accountFolders.FirstOrDefault(f => f.SpecialUse == targetUse);
        if (target is null)
            return $"{DescribeSpecialUse(targetUse)}フォルダが見つかりません。";

        return await MoveToFolderAsync(message, target, ct);
    }

    public async Task<string?> MoveToFolderAsync(MailMessage message, MailFolder target, CancellationToken ct)
    {
        if (target.Id == message.FolderId)
            return null;
        if (target.ImapFullName is null)
            return "このフォルダには移動できません。";

        var sourceRef = new OutboxMessageRef(message.FolderId, message.Uid);
        var sourceFolder = await folderStore.GetByIdAsync(message.FolderId, ct);

        var moved = WithFolder(message, target.Id);
        await messageStore.SaveAsync(moved, ct);

        if (sourceFolder is not null)
        {
            var newUnread = message.IsRead ? sourceFolder.UnreadCount : Math.Max(0, sourceFolder.UnreadCount - 1);
            await folderStore.UpdateCountsAsync(sourceFolder.Id, newUnread, Math.Max(0, sourceFolder.TotalCount - 1), ct);
        }

        await EnqueueOutboxActionAsync(message.AccountId, OutboxActionType.Move, message.Id, target.Id, sourceRef);

        if (message.IsFlagged)
            await mailSyncService.RefreshFlaggedFolderCountAsync(message.AccountId, ct);

        return null;
    }

    private async Task EnqueueOutboxActionAsync(
        Guid accountId, OutboxActionType type, Guid? messageId, Guid? targetFolderId, OutboxMessageRef reference)
    {
        var action = new OutboxAction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            MessageId = messageId,
            TargetFolderId = targetFolderId,
            PayloadJson = JsonSerializer.Serialize(reference),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await outboxStore.EnqueueAsync(action, CancellationToken.None);

        // Best-effort immediate replay; if offline this just no-ops and stays queued.
        _ = outboxProcessor.ProcessAsync(accountId, CancellationToken.None);
    }

    private static string DescribeSpecialUse(MailFolderSpecialUse use) => use switch
    {
        MailFolderSpecialUse.Trash => "ゴミ箱",
        MailFolderSpecialUse.Archive => "アーカイブ",
        _ => use.ToString(),
    };

    private static MailMessage WithFolder(MailMessage message, Guid folderId) => new()
    {
        Id = message.Id,
        AccountId = message.AccountId,
        FolderId = folderId,
        Uid = message.Uid,
        MessageId = message.MessageId,
        InReplyTo = message.InReplyTo,
        References = message.References,
        Subject = message.Subject,
        FromDisplay = message.FromDisplay,
        FromAddress = message.FromAddress,
        ToRecipients = message.ToRecipients,
        CcRecipients = message.CcRecipients,
        Date = message.Date,
        Snippet = message.Snippet,
        IsRead = message.IsRead,
        IsFlagged = message.IsFlagged,
        IsAnswered = message.IsAnswered,
        IsDraft = message.IsDraft,
        HasAttachments = message.HasAttachments,
        Size = message.Size,
        IsBodyDownloaded = message.IsBodyDownloaded,
        BodyTextPath = message.BodyTextPath,
        BodyHtmlPath = message.BodyHtmlPath,
    };
}
