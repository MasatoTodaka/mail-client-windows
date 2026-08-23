using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Search;
using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MailClient.App.Views.Shell;

public sealed partial class MessageListView : UserControl
{
    public MessageListViewModel ViewModel { get; }
    public SearchViewModel SearchViewModel { get; }

    public MessageListView()
    {
        ViewModel = App.Services.GetRequiredService<MessageListViewModel>();
        SearchViewModel = App.Services.GetRequiredService<SearchViewModel>();
        InitializeComponent();
    }

    private void OnMessageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is MailMessage message)
            ViewModel.SelectMessageCommand.Execute(message);
    }

    // A search hit opens in the reading pane through the same MessageSelected path a normal
    // folder-list click uses.
    private void OnSearchResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is SearchResult result)
            ViewModel.SelectMessageCommand.Execute(result.Message);
    }

    private Visibility FolderHeaderVisibility(bool isSearchActive) =>
        isSearchActive ? Visibility.Collapsed : Visibility.Visible;

    // ContextRequested (not RightTapped) so this also responds to keyboard invocation
    // (Shift+F10 / the Menu key) and screen readers, not just pointer right-click.
    private void OnMessageContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MailMessage message } element)
            return;

        ViewModel.SelectMessageCommand.Execute(message);

        var menu = new MenuFlyout();

        var flagItem = new MenuFlyoutItem { Text = message.IsFlagged ? "フラグを外す" : "フラグを付ける" };
        flagItem.Click += async (_, _) => await ViewModel.ToggleFlagOnMessageAsync(message);
        menu.Items.Add(flagItem);

        var readItem = new MenuFlyoutItem { Text = message.IsRead ? "未読にする" : "既読にする" };
        readItem.Click += async (_, _) => await ViewModel.ToggleReadOnMessageAsync(message);
        menu.Items.Add(readItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        // Starts disabled/empty and is populated once the (async, DB-backed) folder list comes
        // back — shows synchronously below so it isn't racing the ListView's virtualized
        // containers being recycled out from under a stale element reference while it awaits.
        var moveSubItem = new MenuFlyoutSubItem { Text = "移動", IsEnabled = false };
        menu.Items.Add(moveSubItem);

        var archiveItem = new MenuFlyoutItem { Text = "アーカイブ" };
        archiveItem.Click += async (_, _) => await ViewModel.ArchiveMessageAsync(message);
        menu.Items.Add(archiveItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var deleteItem = new MenuFlyoutItem { Text = "削除" };
        deleteItem.Click += async (_, _) => await ViewModel.DeleteMessageAsync(message);
        menu.Items.Add(deleteItem);

        if (e.TryGetPosition(element, out var point))
            menu.ShowAt(element, point);
        else
            menu.ShowAt(element);

        _ = PopulateMoveSubmenuAsync(moveSubItem, message);
    }

    private async Task PopulateMoveSubmenuAsync(MenuFlyoutSubItem moveSubItem, MailMessage message)
    {
        var targets = await ViewModel.GetMoveTargetFoldersAsync(message.AccountId, message.FolderId);
        foreach (var target in targets)
        {
            var targetItem = new MenuFlyoutItem { Text = target.DisplayName };
            targetItem.Click += async (_, _) => await ViewModel.MoveMessageAsync(message, target);
            moveSubItem.Items.Add(targetItem);
        }
        moveSubItem.IsEnabled = targets.Count > 0;
    }
}
