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
    private MainWindow? _mainWindow;

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
                services.AddSingleton<INotificationService, AppNotificationService>();

                // Captured lazily: first resolution happens during MainWindow's construction in
                // OnLaunched, which runs on the UI thread, so GetForCurrentThread() returns the
                // app's one-and-only UI dispatcher queue.
                services.AddSingleton<IUiDispatcher>(_ =>
                    new DispatcherQueueUiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            })
            .Build();

        Services = _host.Services;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Services.GetRequiredService<MailDbContext>().MigrateAsync();

        _mainWindow = new MainWindow();
        _mainWindow.Activate();

        // Notifications are best-effort: a failure to register must never take the app down,
        // so this whole block sits after window creation and inside its own try.
        try
        {
            var notifications = Services.GetRequiredService<INotificationService>();
            notifications.Register();

            // New mail (from M8's IDLE/polling watchers) becomes a toast while the window isn't
            // in the foreground; clicking the toast brings the window up on that message.
            var syncService = Services.GetRequiredService<IMailSyncService>();
            var accountStore = Services.GetRequiredService<IAccountStore>();
            var settingsStore = Services.GetRequiredService<ISettingsStore>();
            syncService.MessageArrived += async (_, e) =>
            {
                try
                {
                    if (_mainWindow is null || _mainWindow.IsWindowActive)
                        return;

                    if (!await settingsStore.GetNotificationsEnabledAsync(CancellationToken.None))
                        return;

                    var account = await accountStore.GetByIdAsync(e.Message.AccountId, CancellationToken.None);
                    if (account is not null)
                        notifications.ShowNewMailNotification(e.Message, account);
                }
                catch
                {
                    // A failed toast is not worth disturbing sync for.
                }
            };

            notifications.NotificationInvoked += (_, messageId) =>
            {
                // Toast activation can arrive on a non-UI thread — marshal onto the window's queue.
                _mainWindow?.DispatcherQueue.TryEnqueue(async () =>
                {
                    _mainWindow.Activate();
                    await _mainWindow.OpenMessageAsync(messageId);
                });
            };
        }
        catch
        {
            // Notifications are a nice-to-have; a registration failure must not take the app down.
        }
    }
}
