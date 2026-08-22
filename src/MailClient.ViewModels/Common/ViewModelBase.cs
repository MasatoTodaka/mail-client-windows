using CommunityToolkit.Mvvm.ComponentModel;

namespace MailClient.ViewModels.Common;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;
}
