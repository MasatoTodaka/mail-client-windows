using System.Text.Json;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;

namespace MailClient.Mail.Rules;

public sealed class MailRuleEngine(
    IMailRuleStore ruleStore,
    IMessageStore messageStore,
    IFolderStore folderStore,
    IOutboxStore outboxStore,
    IOutboxProcessor outboxProcessor) : IMailRuleEngine
{
    public async Task<bool> TryApplyAsync(MailMessage message, CancellationToken ct)
    {
        var rules = await ruleStore.GetByAccountAsync(message.AccountId, ct);
        var rule = rules.Where(r => Matches(r, message)).MinBy(r => r.SortOrder);
        if (rule is null || rule.TargetFolderId == message.FolderId)
            return false;

        await MoveAsync(message, rule.TargetFolderId, ct);
        return true;
    }

    public async Task<int> RunNowAsync(Guid accountId, CancellationToken ct)
    {
        var rules = await ruleStore.GetByAccountAsync(accountId, ct);
        if (rules.Count == 0)
            return 0;

        var messages = await messageStore.GetByAccountAsync(accountId, ct);
        var movedCount = 0;
        foreach (var message in messages)
        {
            var rule = rules.Where(r => Matches(r, message)).MinBy(r => r.SortOrder);
            if (rule is null || rule.TargetFolderId == message.FolderId)
                continue;

            await MoveAsync(message, rule.TargetFolderId, ct);
            movedCount++;
        }
        return movedCount;
    }

    // Mirrors ReadingPaneViewModel.MoveToSpecialFolderAsync's optimistic-local-update +
    // Outbox-replay pattern, since a rule-triggered move must survive being offline the same way
    // a user-initiated one does.
    private async Task MoveAsync(MailMessage message, Guid targetFolderId, CancellationToken ct)
    {
        var sourceRef = new OutboxMessageRef(message.FolderId, message.Uid);
        var sourceFolder = await folderStore.GetByIdAsync(message.FolderId, ct);

        var moved = WithFolder(message, targetFolderId);
        await messageStore.SaveAsync(moved, ct);

        if (sourceFolder is not null)
        {
            var newUnread = message.IsRead ? sourceFolder.UnreadCount : Math.Max(0, sourceFolder.UnreadCount - 1);
            await folderStore.UpdateCountsAsync(sourceFolder.Id, newUnread, Math.Max(0, sourceFolder.TotalCount - 1), ct);
        }

        var action = new OutboxAction
        {
            Id = Guid.NewGuid(),
            AccountId = message.AccountId,
            Type = OutboxActionType.Move,
            MessageId = message.Id,
            TargetFolderId = targetFolderId,
            PayloadJson = JsonSerializer.Serialize(sourceRef),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await outboxStore.EnqueueAsync(action, ct);

        // Best-effort immediate replay; if offline this just no-ops and stays queued.
        _ = outboxProcessor.ProcessAsync(message.AccountId, CancellationToken.None);
    }

    private static bool Matches(MailRule rule, MailMessage message)
    {
        var hasSender = !string.IsNullOrWhiteSpace(rule.SenderContains);
        var hasSubject = !string.IsNullOrWhiteSpace(rule.SubjectContains);
        if (!hasSender && !hasSubject)
            return false; // a rule with no conditions would match everything -- refuse it

        if (hasSender &&
            !message.FromAddress.Contains(rule.SenderContains!, StringComparison.OrdinalIgnoreCase) &&
            !message.FromDisplay.Contains(rule.SenderContains!, StringComparison.OrdinalIgnoreCase))
            return false;

        if (hasSubject && !message.Subject.Contains(rule.SubjectContains!, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

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
