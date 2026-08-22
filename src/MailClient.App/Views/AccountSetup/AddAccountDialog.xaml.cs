using MailClient.ViewModels.AccountSetup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailClient.App.Views.AccountSetup;

public sealed partial class AddAccountDialog : ContentDialog
{
    public AddAccountViewModel ViewModel { get; }

    public AddAccountDialog(AddAccountViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.SaveCommand.ExecuteAsync(null);

            // Validation/save failed: keep the dialog open so the user sees ErrorMessage and can retry.
            if (!string.IsNullOrEmpty(ViewModel.ErrorMessage))
                args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private Visibility ErrorMessageVisibility(string? errorMessage) =>
        string.IsNullOrEmpty(errorMessage) ? Visibility.Collapsed : Visibility.Visible;
}
