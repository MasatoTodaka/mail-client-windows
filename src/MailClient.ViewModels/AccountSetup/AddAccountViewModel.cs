using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.AccountSetup;

// M2: form only, no network round-trip. IMAP/SMTP connectivity is validated starting M3.
public sealed partial class AddAccountViewModel(IAccountStore accountStore, ICredentialStore credentialStore) : ViewModelBase
{
    public SecurityMode[] SecurityModeOptions { get; } = Enum.GetValues<SecurityMode>();

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _emailAddress = string.Empty;

    [ObservableProperty]
    private string _imapHost = string.Empty;

    [ObservableProperty]
    private string _imapPortText = "993";

    [ObservableProperty]
    private SecurityMode _imapSecurity = SecurityMode.SslOnConnect;

    [ObservableProperty]
    private string _imapUsername = string.Empty;

    [ObservableProperty]
    private string _smtpHost = string.Empty;

    [ObservableProperty]
    private string _smtpPortText = "465";

    [ObservableProperty]
    private SecurityMode _smtpSecurity = SecurityMode.SslOnConnect;

    [ObservableProperty]
    private string _smtpUsername = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    public Guid? SavedAccountId { get; private set; }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (!TryValidate(out var imapPort, out var smtpPort))
            return;

        IsBusy = true;
        try
        {
            var account = new Account
            {
                Id = Guid.NewGuid(),
                DisplayName = DisplayName.Trim(),
                EmailAddress = EmailAddress.Trim(),
                ImapHost = ImapHost.Trim(),
                ImapPort = imapPort,
                ImapSecurity = ImapSecurity,
                ImapUsername = ImapUsername.Trim(),
                SmtpHost = SmtpHost.Trim(),
                SmtpPort = smtpPort,
                SmtpSecurity = SmtpSecurity,
                SmtpUsername = SmtpUsername.Trim(),
            };

            await accountStore.SaveAsync(account, CancellationToken.None);
            credentialStore.SaveImapPassword(account.Id, Password);
            credentialStore.SaveSmtpPassword(account.Id, Password);

            SavedAccountId = account.Id;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryValidate(out int imapPort, out int smtpPort)
    {
        imapPort = 0;
        smtpPort = 0;

        if (string.IsNullOrWhiteSpace(DisplayName) ||
            string.IsNullOrWhiteSpace(EmailAddress) ||
            string.IsNullOrWhiteSpace(ImapHost) ||
            string.IsNullOrWhiteSpace(ImapUsername) ||
            string.IsNullOrWhiteSpace(SmtpHost) ||
            string.IsNullOrWhiteSpace(SmtpUsername) ||
            string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "すべての必須項目を入力してください。";
            return false;
        }

        if (!int.TryParse(ImapPortText, out imapPort) || imapPort is <= 0 or > 65535)
        {
            ErrorMessage = "IMAPポート番号が不正です。";
            return false;
        }

        if (!int.TryParse(SmtpPortText, out smtpPort) || smtpPort is <= 0 or > 65535)
        {
            ErrorMessage = "SMTPポート番号が不正です。";
            return false;
        }

        return true;
    }
}
