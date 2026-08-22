using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MailClient.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();

        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
    }
}
