using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;
using MimeKit;

namespace MailClient.ViewModels.Compose;

// M6: builds a real RFC 5322 message (via MimeKit) and queues it as an Outbox SendMessage
// action. Actual delivery is wired up in M7 once ISmtpSender exists — until then the action
// simply stays queued (OutboxProcessor reports it as not-yet-supported on each retry).
public sealed partial class ComposeViewModel(
    IAccountStore accountStore,
    IOutboxStore outboxStore,
    IOutboxProcessor outboxProcessor,
    AppDataPaths appDataPaths) : ViewModelBase
{
    private Guid _accountId;

    [ObservableProperty]
    private string _to = string.Empty;

    [ObservableProperty]
    private string _cc = string.Empty;

    [ObservableProperty]
    private string _subject = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private bool _isQueued;

    public void Prepare(ComposeMode mode, MailMessage sourceMessage, string? quotedBodyText)
    {
        _accountId = sourceMessage.AccountId;

        var quotedHeader = $"{sourceMessage.FromDisplay} ({sourceMessage.Date:yyyy/MM/dd HH:mm}) wrote:";
        var quotedBody = string.Join(
            '\n',
            (quotedBodyText ?? string.Empty).Split('\n').Select(line => $"> {line}"));

        switch (mode)
        {
            case ComposeMode.Reply:
                To = sourceMessage.FromAddress;
                Subject = EnsurePrefix(sourceMessage.Subject, "Re: ");
                Body = $"\n\n{quotedHeader}\n{quotedBody}";
                break;

            case ComposeMode.ReplyAll:
                To = string.Join("; ", new[] { sourceMessage.FromAddress }
                    .Concat(SplitAddresses(sourceMessage.ToRecipients))
                    .Concat(SplitAddresses(sourceMessage.CcRecipients))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                Subject = EnsurePrefix(sourceMessage.Subject, "Re: ");
                Body = $"\n\n{quotedHeader}\n{quotedBody}";
                break;

            case ComposeMode.Forward:
                To = string.Empty;
                Subject = EnsurePrefix(sourceMessage.Subject, "Fwd: ");
                Body = $"\n\n--- Forwarded message ---\nFrom: {sourceMessage.FromDisplay} <{sourceMessage.FromAddress}>\nSubject: {sourceMessage.Subject}\n\n{quotedBodyText}";
                break;
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(To))
        {
            ErrorMessage = "宛先を入力してください。";
            return;
        }

        var account = await accountStore.GetByIdAsync(_accountId, CancellationToken.None);
        if (account is null)
        {
            ErrorMessage = "送信元アカウントが見つかりません。";
            return;
        }

        IsBusy = true;
        try
        {
            var mimeMessage = new MimeMessage();
            mimeMessage.Date = DateTimeOffset.Now;
            mimeMessage.From.Add(MailboxAddress.Parse(account.EmailAddress));
            foreach (var address in SplitAddresses(To))
                mimeMessage.To.Add(MailboxAddress.Parse(address));
            foreach (var address in SplitAddresses(Cc))
                mimeMessage.Cc.Add(MailboxAddress.Parse(address));
            mimeMessage.Subject = Subject;
            mimeMessage.Body = new TextPart("plain") { Text = Body };

            Directory.CreateDirectory(appDataPaths.OutboxDirectory);
            var actionId = Guid.NewGuid();
            var emlPath = Path.Combine(appDataPaths.OutboxDirectory, $"{actionId}.eml");
            await using (var stream = File.Create(emlPath))
                await mimeMessage.WriteToAsync(stream);

            var action = new OutboxAction
            {
                Id = actionId,
                AccountId = account.Id,
                Type = OutboxActionType.SendMessage,
                PayloadJson = JsonSerializer.Serialize(new OutboxSendPayload(emlPath)),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await outboxStore.EnqueueAsync(action, CancellationToken.None);
            _ = outboxProcessor.ProcessAsync(account.Id, CancellationToken.None);

            IsQueued = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"送信キューへの登録に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string EnsurePrefix(string subject, string prefix) =>
        subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? subject : prefix + subject;

    private static IEnumerable<string> SplitAddresses(string? joined) =>
        (joined ?? string.Empty)
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
