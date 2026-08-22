using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Rules;

// Per-account sender/subject rules that file matching new mail into a folder automatically
// (applied on arrival by IMailRuleEngine), with a "run now" to apply retroactively.
public sealed partial class RulesViewModel(
    IAccountStore accountStore,
    IFolderStore folderStore,
    IMailRuleStore ruleStore,
    IMailRuleEngine ruleEngine) : ViewModelBase
{
    public ObservableCollection<Account> Accounts { get; } = [];
    public ObservableCollection<MailFolder> FoldersForSelectedAccount { get; } = [];
    public ObservableCollection<RuleDisplayItem> Rules { get; } = [];

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private MailFolder? _selectedTargetFolder;

    [ObservableProperty]
    private string _senderContains = string.Empty;

    [ObservableProperty]
    private string _subjectContains = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    [RelayCommand]
    public async Task LoadAsync()
    {
        Accounts.Clear();
        foreach (var account in await accountStore.GetAllAsync(CancellationToken.None))
            Accounts.Add(account);

        SelectedAccount = Accounts.FirstOrDefault();
        if (SelectedAccount is null)
            await LoadFoldersAndRulesAsync();
    }

    partial void OnSelectedAccountChanged(Account? value) => _ = LoadFoldersAndRulesAsync();

    private async Task LoadFoldersAndRulesAsync()
    {
        FoldersForSelectedAccount.Clear();
        Rules.Clear();
        StatusMessage = null;
        ErrorMessage = null;

        if (SelectedAccount is null)
            return;

        var folders = await folderStore.GetByAccountAsync(SelectedAccount.Id, CancellationToken.None);
        foreach (var folder in folders)
            FoldersForSelectedAccount.Add(folder);
        SelectedTargetFolder = FoldersForSelectedAccount.FirstOrDefault();

        var foldersById = folders.ToDictionary(f => f.Id);
        var rules = await ruleStore.GetByAccountAsync(SelectedAccount.Id, CancellationToken.None);
        foreach (var rule in rules)
        {
            var folderName = foldersById.TryGetValue(rule.TargetFolderId, out var folder) ? folder.DisplayName : "(不明なフォルダ)";
            Rules.Add(new RuleDisplayItem(rule, folderName));
        }
    }

    [RelayCommand]
    private async Task AddRuleAsync()
    {
        ErrorMessage = null;
        if (SelectedAccount is null || SelectedTargetFolder is null)
            return;

        if (string.IsNullOrWhiteSpace(SenderContains) && string.IsNullOrWhiteSpace(SubjectContains))
        {
            ErrorMessage = "送信者か件名のどちらかを入力してください。";
            return;
        }

        var rule = new MailRule
        {
            Id = Guid.NewGuid(),
            AccountId = SelectedAccount.Id,
            SenderContains = string.IsNullOrWhiteSpace(SenderContains) ? null : SenderContains.Trim(),
            SubjectContains = string.IsNullOrWhiteSpace(SubjectContains) ? null : SubjectContains.Trim(),
            TargetFolderId = SelectedTargetFolder.Id,
            SortOrder = Rules.Count,
        };
        await ruleStore.SaveAsync(rule, CancellationToken.None);
        Rules.Add(new RuleDisplayItem(rule, SelectedTargetFolder.DisplayName));

        SenderContains = string.Empty;
        SubjectContains = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(RuleDisplayItem item)
    {
        await ruleStore.DeleteAsync(item.Rule.Id, CancellationToken.None);
        Rules.Remove(item);
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        if (SelectedAccount is null)
            return;

        StatusMessage = null;
        IsBusy = true;
        try
        {
            var count = await ruleEngine.RunNowAsync(SelectedAccount.Id, CancellationToken.None);
            StatusMessage = count == 0 ? "移動したメールはありませんでした。" : $"{count}件のメールを移動しました。";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"実行に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
