using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using MailClient.ViewModels.Search;
using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
}
