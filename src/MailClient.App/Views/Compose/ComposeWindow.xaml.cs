using MailClient.ViewModels.Compose;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace MailClient.App.Views.Compose;

public sealed partial class ComposeWindow : Window
{
    public ComposeViewModel ViewModel { get; }

    public ComposeWindow(ComposeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        App.Services.GetRequiredService<ThemeService>().Register(this);

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
