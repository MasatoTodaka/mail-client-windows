using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.Core.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using MailFolder = MailClient.Core.Models.MailFolder;

namespace MailClient.Mail.Imap;

public sealed class ImapAccountClient : IImapAccountClient
{
    private readonly ImapClient _client = new();
    private Guid _accountId;

    public bool SupportsIdle => _client.IsConnected && _client.Capabilities.HasFlag(ImapCapabilities.Idle);

    public async Task ConnectAsync(Account account, string password, CancellationToken ct)
    {
        _accountId = account.Id;
        var options = account.ImapSecurity switch
        {
            SecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SecurityMode.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None,
        };

        try
        {
            await _client.ConnectAsync(account.ImapHost, account.ImapPort, options, ct);
            await _client.AuthenticateAsync(account.ImapUsername, password, ct);
        }
        catch (AuthenticationException ex)
        {
            throw new ImapAuthenticationException("IMAP認証に失敗しました。ユーザー名またはパスワードを確認してください。", ex);
        }
        catch (Exception ex) when (ex is not ImapAuthenticationException and not OperationCanceledException)
        {
            throw new ImapConnectionException($"IMAPサーバーに接続できませんでした: {ex.Message}", ex);
        }
    }

    public Task DisconnectAsync() => _client.DisconnectAsync(true);

    public async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(CancellationToken ct)
    {
        var personalNamespace = _client.PersonalNamespaces[0];
        var root = await _client.GetFolderAsync(personalNamespace.Path, ct);
        var imapFolders = await root.GetSubfoldersAsync(true, ct);

        // Some servers (e.g. iCloud) don't return INBOX from GetSubfoldersAsync — it's always
        // reachable via the client's own Inbox property, so fold it in explicitly.
        if (imapFolders.All(f => !string.Equals(f.FullName, _client.Inbox.FullName, StringComparison.OrdinalIgnoreCase)))
            imapFolders = [_client.Inbox, .. imapFolders];

        var result = new List<MailFolder>();
        foreach (var folder in imapFolders)
        {
            if (folder.Attributes.HasFlag(FolderAttributes.NonExistent) || folder.Attributes.HasFlag(FolderAttributes.NoSelect))
                continue;

            result.Add(new MailFolder
            {
                Id = Guid.NewGuid(),
                AccountId = _accountId,
                ImapFullName = folder.FullName,
                DisplayName = folder.Name,
                SpecialUse = MapSpecialUse(folder.Attributes, folder.Name),
            });
        }
        return result;
    }

    public async Task<(uint UidValidity, uint UidNext)> SelectFolderAsync(string imapFullName, CancellationToken ct)
    {
        var folder = await _client.GetFolderAsync(imapFullName, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        return (folder.UidValidity, folder.UidNext?.Id ?? 0u);
    }

    public async Task<IReadOnlyList<MailMessage>> FetchHeadersAsync(
        string imapFullName, uint fromUid, uint? toUid, int maxCount, CancellationToken ct)
    {
        var folder = await _client.GetFolderAsync(imapFullName, ct);
        if (!folder.IsOpen)
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var range = toUid.HasValue
            ? new UniqueIdRange(new UniqueId(fromUid), new UniqueId(toUid.Value))
            : new UniqueIdRange(new UniqueId(fromUid), UniqueId.MaxValue);

        var summaries = await folder.FetchAsync(
            range,
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags
                | MessageSummaryItems.Size | MessageSummaryItems.BodyStructure | MessageSummaryItems.References,
            ct);

        return summaries
            .OrderByDescending(s => s.UniqueId.Id)
            .Take(maxCount)
            .Select(MapSummary)
            .ToList();
    }

    public async Task<(string? TextBody, string? HtmlBody)> FetchBodyAsync(string imapFullName, uint uid, CancellationToken ct)
    {
        var folder = await _client.GetFolderAsync(imapFullName, ct);
        if (!folder.IsOpen)
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var message = await folder.GetMessageAsync(new UniqueId(uid), ct);
        return (message.TextBody, message.HtmlBody);
    }

    public async Task SetFlagsAsync(string imapFullName, uint uid, bool isRead, bool isFlagged, CancellationToken ct)
    {
        var folder = await _client.GetFolderAsync(imapFullName, ct);
        if (!folder.IsOpen)
            await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        var flags = MessageFlags.None;
        if (isRead) flags |= MessageFlags.Seen;
        if (isFlagged) flags |= MessageFlags.Flagged;

        await folder.SetFlagsAsync(new UniqueId(uid), flags, true, ct);
    }

    public async Task MoveAsync(string imapFullName, uint uid, string destinationFullName, CancellationToken ct)
    {
        var folder = await _client.GetFolderAsync(imapFullName, ct);
        if (!folder.IsOpen)
            await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        var destination = await _client.GetFolderAsync(destinationFullName, ct);
        await folder.MoveToAsync(new UniqueId(uid), destination, ct);
    }

    public async Task DeleteAsync(string imapFullName, uint uid, CancellationToken ct)
    {
        var folder = await _client.GetFolderAsync(imapFullName, ct);
        if (!folder.IsOpen)
            await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        await folder.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, true, ct);
        await folder.ExpungeAsync(ct);
    }

    public async Task AppendAsync(string imapFullName, byte[] mimeMessage, CancellationToken ct)
    {
        var folder = await _client.GetFolderAsync(imapFullName, ct);
        using var stream = new MemoryStream(mimeMessage);
        var message = await MimeMessage.LoadAsync(stream, ct);
        await folder.AppendAsync(message, MessageFlags.Seen, ct);
    }

    private static MailFolderSpecialUse MapSpecialUse(FolderAttributes attributes, string displayName)
    {
        if (attributes.HasFlag(FolderAttributes.Inbox)) return MailFolderSpecialUse.Inbox;
        if (attributes.HasFlag(FolderAttributes.Sent)) return MailFolderSpecialUse.Sent;
        if (attributes.HasFlag(FolderAttributes.Drafts)) return MailFolderSpecialUse.Drafts;
        if (attributes.HasFlag(FolderAttributes.Trash)) return MailFolderSpecialUse.Trash;
        if (attributes.HasFlag(FolderAttributes.Junk)) return MailFolderSpecialUse.Junk;
        if (attributes.HasFlag(FolderAttributes.Archive)) return MailFolderSpecialUse.Archive;

        // Some servers (e.g. iCloud) don't advertise RFC 6154 SPECIAL-USE attributes at all —
        // fall back to conventional folder names, same as most real-world mail clients do.
        // Where a server exposes more than one plausible name (e.g. both "Sent Items" and
        // "Sent Messages"), prefer the legacy Apple/Cyrus-style name since it's the one iCloud
        // actually files mail into.
        return displayName switch
        {
            "Sent Messages" or "Sent" => MailFolderSpecialUse.Sent,
            "Deleted Messages" or "Trash" => MailFolderSpecialUse.Trash,
            "Drafts" => MailFolderSpecialUse.Drafts,
            "Junk" or "Junk E-mail" or "迷惑メール" => MailFolderSpecialUse.Junk,
            "Archive" or "All Mail" => MailFolderSpecialUse.Archive,
            _ => MailFolderSpecialUse.None,
        };
    }

    private MailMessage MapSummary(IMessageSummary summary) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = _accountId,
        FolderId = Guid.Empty, // caller (MailSyncService, M4) fills in the real FolderId when persisting
        Uid = summary.UniqueId.Id,
        MessageId = summary.Envelope?.MessageId,
        InReplyTo = summary.Envelope?.InReplyTo,
        References = summary.References is { Count: > 0 } refs ? string.Join(' ', refs) : null,
        Subject = SubjectCharsetFixer.Fix(summary.Envelope?.Subject ?? string.Empty),
        FromDisplay = summary.Envelope?.From?.Mailboxes?.FirstOrDefault()?.Name ?? string.Empty,
        FromAddress = summary.Envelope?.From?.Mailboxes?.FirstOrDefault()?.Address ?? string.Empty,
        ToRecipients = summary.Envelope?.To is { Count: > 0 } to
            ? string.Join(';', to.Mailboxes.Select(m => m.Address))
            : string.Empty,
        CcRecipients = summary.Envelope?.Cc is { Count: > 0 } cc
            ? string.Join(';', cc.Mailboxes.Select(m => m.Address))
            : null,
        Date = summary.Envelope?.Date ?? DateTimeOffset.UtcNow,
        IsRead = summary.Flags?.HasFlag(MessageFlags.Seen) ?? false,
        IsFlagged = summary.Flags?.HasFlag(MessageFlags.Flagged) ?? false,
        IsAnswered = summary.Flags?.HasFlag(MessageFlags.Answered) ?? false,
        IsDraft = summary.Flags?.HasFlag(MessageFlags.Draft) ?? false,
        HasAttachments = summary.Attachments?.Any() ?? false,
        Size = (int)(summary.Size ?? 0),
    };

    public void Dispose() => _client.Dispose();
}
