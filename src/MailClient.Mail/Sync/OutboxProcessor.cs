using System.Text.Json;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;

namespace MailClient.Mail.Sync;

// Replays queued mutations (flag/read state, move, delete, send) against the server once
// connectivity is available. Each action is attempted independently — one failure doesn't block
// the rest — and a succeeded action is removed from the queue by IOutboxStore.MarkSucceededAsync.
public sealed class OutboxProcessor(
    IAccountStore accountStore,
    IFolderStore folderStore,
    IMessageStore messageStore,
    IOutboxStore outboxStore,
    ICredentialStore credentialStore,
    ISmtpSender smtpSender,
    Func<IImapAccountClient> imapClientFactory) : IOutboxProcessor
{
    public async Task ProcessAsync(Guid accountId, CancellationToken ct)
    {
        var pending = await outboxStore.GetPendingAsync(accountId, ct);
        if (pending.Count == 0)
            return;

        var account = await accountStore.GetByIdAsync(accountId, ct);
        var password = account is null ? null : credentialStore.GetImapPassword(accountId);
        if (account is null || password is null)
            return;

        using var client = imapClientFactory();
        try
        {
            await client.ConnectAsync(account, password, ct);
        }
        catch (Exception)
        {
            // Still offline (or credentials changed) — leave everything queued for next time.
            return;
        }

        foreach (var action in pending)
        {
            try
            {
                await ApplyAsync(client, account, action, ct);
                await outboxStore.MarkSucceededAsync(action.Id, ct);
            }
            catch (Exception ex)
            {
                await outboxStore.MarkFailedAsync(action.Id, ex.Message, ct);
            }
        }

        await client.DisconnectAsync();
    }

    private async Task ApplyAsync(IImapAccountClient client, Account account, OutboxAction action, CancellationToken ct)
    {
        switch (action.Type)
        {
            case OutboxActionType.MarkRead:
            case OutboxActionType.MarkUnread:
            case OutboxActionType.MarkFlagged:
            case OutboxActionType.MarkUnflagged:
                await ApplyFlagChangeAsync(client, action, ct);
                break;

            case OutboxActionType.Move:
                await ApplyMoveAsync(client, action, ct);
                break;

            case OutboxActionType.Delete:
                await ApplyDeleteAsync(client, action, ct);
                break;

            case OutboxActionType.SendMessage:
                await ApplySendMessageAsync(client, account, action, ct);
                break;

            case OutboxActionType.Append:
                throw new NotSupportedException("Appendは未実装です。");

            default:
                throw new NotSupportedException($"未知のOutboxActionType: {action.Type}");
        }
    }

    private async Task ApplyFlagChangeAsync(IImapAccountClient client, OutboxAction action, CancellationToken ct)
    {
        var (folderFullName, uid) = await ResolveMessageRefAsync(action, ct);

        // SetFlagsAsync takes the full desired (isRead, isFlagged) state, but this action only
        // knows about ONE of those two dimensions changing — read the current message row for
        // the other one so we don't clobber it.
        var message = action.MessageId is { } messageId ? await MessageOrNullAsync(messageId, ct) : null;

        var isRead = action.Type switch
        {
            OutboxActionType.MarkRead => true,
            OutboxActionType.MarkUnread => false,
            _ => message?.IsRead ?? false,
        };
        var isFlagged = action.Type switch
        {
            OutboxActionType.MarkFlagged => true,
            OutboxActionType.MarkUnflagged => false,
            _ => message?.IsFlagged ?? false,
        };

        await client.SetFlagsAsync(folderFullName, uid, isRead, isFlagged, ct);
    }

    private async Task ApplyMoveAsync(IImapAccountClient client, OutboxAction action, CancellationToken ct)
    {
        var (folderFullName, uid) = await ResolveMessageRefAsync(action, ct);
        var targetFolder = action.TargetFolderId is { } targetId ? await folderStore.GetByIdAsync(targetId, ct) : null;
        if (targetFolder?.ImapFullName is null)
            throw new InvalidOperationException("移動先フォルダが見つかりません。");

        await client.MoveAsync(folderFullName, uid, targetFolder.ImapFullName, ct);
    }

    private async Task ApplyDeleteAsync(IImapAccountClient client, OutboxAction action, CancellationToken ct)
    {
        var (folderFullName, uid) = await ResolveMessageRefAsync(action, ct);
        await client.DeleteAsync(folderFullName, uid, ct);
    }

    private async Task ApplySendMessageAsync(IImapAccountClient client, Account account, OutboxAction action, CancellationToken ct)
    {
        if (action.PayloadJson is null)
            throw new InvalidOperationException("Outboxアクションに送信内容がありません。");

        var payload = JsonSerializer.Deserialize<OutboxSendPayload>(action.PayloadJson)
            ?? throw new InvalidOperationException("送信内容を読み取れません。");
        if (!File.Exists(payload.EmlFilePath))
            throw new InvalidOperationException("送信するメッセージファイルが見つかりません。");

        var smtpPassword = credentialStore.GetSmtpPassword(account.Id)
            ?? throw new SmtpAuthenticationException("SMTP用の保存されたパスワードが見つかりません。");

        var mimeBytes = await File.ReadAllBytesAsync(payload.EmlFilePath, ct);
        await smtpSender.SendAsync(account, smtpPassword, mimeBytes, ct);

        // Best-effort: file a copy in Sent. The message is already delivered at this point, so a
        // missing Sent folder or a server that refuses the APPEND must not turn into a retry
        // (which would resend the message) — swallow and move on.
        try
        {
            var folders = await folderStore.GetByAccountAsync(account.Id, ct);
            var sent = folders.FirstOrDefault(f => f.SpecialUse == MailFolderSpecialUse.Sent);
            if (sent?.ImapFullName is not null)
                await client.AppendAsync(sent.ImapFullName, mimeBytes, ct);
        }
        catch
        {
            // Ignored — see comment above.
        }

        File.Delete(payload.EmlFilePath);
    }

    private async Task<(string FolderFullName, uint Uid)> ResolveMessageRefAsync(OutboxAction action, CancellationToken ct)
    {
        if (action.PayloadJson is null)
            throw new InvalidOperationException("Outboxアクションに位置情報がありません。");

        var reference = JsonSerializer.Deserialize<OutboxMessageRef>(action.PayloadJson)
            ?? throw new InvalidOperationException("Outboxアクションの位置情報を読み取れません。");

        var folder = await folderStore.GetByIdAsync(reference.FolderId, ct);
        if (folder?.ImapFullName is null)
            throw new InvalidOperationException("元のフォルダが見つかりません。");

        return (folder.ImapFullName, reference.Uid);
    }

    // Only used to read the sibling flag (read/flagged) that a given action doesn't itself carry.
    private Task<MailMessage?> MessageOrNullAsync(Guid messageId, CancellationToken ct) =>
        messageStore.GetByIdAsync(messageId, ct);
}
