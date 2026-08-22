using MailClient.Core.Abstractions;
using MailClient.Data;
using MailClient.Infrastructure;
using MailClient.Platform;
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
                services.AddSingleton<ICredentialStore, CredentialLockerStore>();
                // M10: INotificationService -> AppNotificationService
            })
            .Build();

        Services = _host.Services;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Services.GetRequiredService<MailDbContext>().MigrateAsync();

        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }
}
