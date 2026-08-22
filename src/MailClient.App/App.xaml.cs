using MailClient.Core.Abstractions;
using MailClient.Core.Text;
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
                services.AddSingleton<IClipboardService, ClipboardService>();

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

        // One-time local cleanup: re-decode any subject or cached body file that predates
        // MojibakeFixer. Local-only and idempotent (no-ops once everything's already fixed), so
        // it's safe to just run on every launch rather than tracking whether it's needed.
        try
        {
            var messageStore = Services.GetRequiredService<IMessageStore>();
            await messageStore.FixMojibakeSubjectsAsync(CancellationToken.None);

            foreach (var message in await messageStore.GetDownloadedBodyMessagesAsync(CancellationToken.None))
            {
                if (message.BodyTextPath is { } textPath && File.Exists(textPath))
                {
                    var text = await File.ReadAllTextAsync(textPath);
                    var fixedText = MojibakeFixer.Fix(text);
                    if (fixedText != text)
                        await File.WriteAllTextAsync(textPath, fixedText);
                }

                if (message.BodyHtmlPath is { } htmlPath && File.Exists(htmlPath))
                {
                    var html = await File.ReadAllTextAsync(htmlPath);
                    var fixedHtml = MojibakeFixer.Fix(html);
                    if (fixedHtml != html)
                        await File.WriteAllTextAsync(htmlPath, fixedHtml);
                }
            }
        }
        catch
        {
            // Best-effort cleanup; must not block startup.
        }

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

        // OTP auto-copy: runs regardless of whether the window is active (unlike the new-mail
        // toast above) — the user may well be sitting in the app when the code arrives and still
        // want it on the clipboard to paste elsewhere.
        try
        {
            var syncService = Services.GetRequiredService<IMailSyncService>();
            var folderStore = Services.GetRequiredService<IFolderStore>();
            var accountStore = Services.GetRequiredService<IAccountStore>();
            var credentialStore = Services.GetRequiredService<ICredentialStore>();
            var imapClientFactory = Services.GetRequiredService<Func<IImapAccountClient>>();
            var settingsStore = Services.GetRequiredService<ISettingsStore>();
            var clipboard = Services.GetRequiredService<IClipboardService>();
            var uiDispatcher = Services.GetRequiredService<IUiDispatcher>();
            var notifications = Services.GetRequiredService<INotificationService>();

            syncService.MessageArrived += async (_, e) =>
            {
                try
                {
                    if (!await settingsStore.GetOtpAutoCopyEnabledAsync(CancellationToken.None))
                        return;

                    // Cheap check first: many senders put the code in the subject too, which
                    // avoids an extra IMAP round trip for those.
                    var code = OtpCodeDetector.TryExtract(e.Message.Subject, null);

                    if (code is null)
                    {
                        var folder = await folderStore.GetByIdAsync(e.Message.FolderId, CancellationToken.None);
                        var account = await accountStore.GetByIdAsync(e.Message.AccountId, CancellationToken.None);
                        var password = account is null ? null : credentialStore.GetImapPassword(account.Id);
                        if (folder?.ImapFullName is null || account is null || password is null)
                            return;

                        using var client = imapClientFactory();
                        await client.ConnectAsync(account, password, CancellationToken.None);
                        var (text, html) = await client.FetchBodyAsync(folder.ImapFullName, e.Message.Uid, CancellationToken.None);
                        await client.DisconnectAsync();

                        var bodyText = text ?? (html is null ? null : StripHtmlTags(html));
                        code = OtpCodeDetector.TryExtract(e.Message.Subject, bodyText);
                    }

                    if (code is null)
                        return;

                    uiDispatcher.Post(() =>
                    {
                        clipboard.SetText(code);
                        try
                        {
                            notifications.ShowOtpCopiedNotification();
                        }
                        catch
                        {
                            // A failed toast is not worth losing the clipboard copy over.
                        }
                    });
                }
                catch
                {
                    // Best-effort: OTP detection must never disturb sync.
                }
            };
        }
        catch
        {
            // Best-effort feature; must not take the app down.
        }

        // Mail rules: files new mail into a folder automatically per the sender/subject rules
        // the user configured. Runs regardless of window focus, same reasoning as OTP auto-copy.
        try
        {
            var syncService = Services.GetRequiredService<IMailSyncService>();
            var ruleEngine = Services.GetRequiredService<IMailRuleEngine>();
            syncService.MessageArrived += async (_, e) =>
            {
                try
                {
                    await ruleEngine.TryApplyAsync(e.Message, CancellationToken.None);
                }
                catch
                {
                    // Best-effort: a failed rule application must never disturb sync.
                }
            };
        }
        catch
        {
            // Best-effort feature; must not take the app down.
        }
    }

    private static string StripHtmlTags(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
}
