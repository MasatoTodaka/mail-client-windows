using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MailClient.Core.Models;

namespace MailClient.ViewModels.Shell;

// One sidebar tree node: an account plus the folders discovered under it.
public sealed partial class AccountNode(Account account) : ObservableObject
{
    public Account Account { get; } = account;
    public string DisplayName => Account.DisplayName;
    public ObservableCollection<MailFolder> Folders { get; } = [];

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _unreadTotal;

    // MailFolder isn't an ObservableObject, so per-folder count changes replace the instance in
    // Folders rather than mutating it in place; call this after any such replacement.
    public void RecalculateUnreadTotal() => UnreadTotal = Folders.Sum(f => f.UnreadCount);
}
