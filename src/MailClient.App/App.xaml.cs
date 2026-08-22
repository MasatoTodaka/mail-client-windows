using MailClient.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace MailClient.App;

public partial class App : Application
{
    private readonly IHost _host;
    private Window? _mainWindow;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddMailClientServices();

                // Windows-only services are registered here, not in MailClient.Infrastructure,
                // because only this project (net8.0-windows) can see MailClient.Platform.
                // Wired up starting M2 (ICredentialStore) and M10 (INotificationService).
            })
            .Build();

        Services = _host.Services;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }
}
