using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MailClient.Core.Abstractions;
using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Search;

// M9: debounced cross-folder full-text search over the FTS5 index. Results replace the
// message list while a query is active; clearing the box returns to the folder view.
public sealed partial class SearchViewModel(ISearchIndex searchIndex) : ViewModelBase
{
    private const int DebounceMilliseconds = 300;
    private const int MaxResults = 100;

    private CancellationTokenSource? _debounceCts;

    public ObservableCollection<SearchResult> Results { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    // True while a non-empty query is active — the view uses this to swap the folder list
    // out for the results list.
    [ObservableProperty]
    private bool _isSearchActive;

    partial void OnSearchTextChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        _ = DebouncedSearchAsync(value, _debounceCts.Token);
    }

    private async Task DebouncedSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, ct);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by more typing
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            Results.Clear();
            IsSearchActive = false;
            ErrorMessage = null;
            return;
        }

        IsSearchActive = true;
        IsBusy = true;
        try
        {
            var results = await searchIndex.SearchAsync(query, accountId: null, MaxResults, ct);
            if (ct.IsCancellationRequested)
                return; // a newer query took over while we were searching

            Results.Clear();
            foreach (var result in results)
                Results.Add(result);
            ErrorMessage = null;
        }
        catch (OperationCanceledException)
        {
            // superseded — the newer query's pass will update the list
        }
        catch (Exception ex)
        {
            ErrorMessage = $"検索に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
