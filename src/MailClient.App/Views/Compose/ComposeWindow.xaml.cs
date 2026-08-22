using MailClient.ViewModels.Compose;
using Microsoft.UI.Xaml;

namespace MailClient.App.Views.Compose;

public sealed partial class ComposeWindow : Window
{
    public ComposeViewModel ViewModel { get; }

    public ComposeWindow(ComposeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ComposeViewModel.IsQueued) && ViewModel.IsQueued)
                Close();
        };
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private Visibility ErrorMessageVisibility(string? errorMessage) =>
        string.IsNullOrEmpty(errorMessage) ? Visibility.Collapsed : Visibility.Visible;
}
