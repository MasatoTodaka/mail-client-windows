using MailClient.Core;
using MailClient.Core.Abstractions;
using MailClient.Data;
using MailClient.Data.Repositories;
using MailClient.Data.Search;
using MailClient.Mail.Imap;
using MailClient.Mail.Smtp;
using MailClient.Mail.Sync;
using MailClient.ViewModels.AccountSetup;
using MailClient.ViewModels.Compose;
using MailClient.ViewModels.Search;
using MailClient.ViewModels.Settings;
using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace MailClient.Infrastructure;

// Cross-platform service registrations (ViewModels, and Data/Mail implementations as they land
// in later milestones). Windows-only services live in MailClient.App's own composition step —
// see the note in MailClient.Infrastructure.csproj.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMailClientServices(this IServiceCollection services)
    {
        services.AddSingleton(_ =>
        {
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MailClient");
            Directory.CreateDirectory(dataDir);
            return new AppDataPaths(dataDir);
        });
        services.AddSingleton(sp => new MailDbContext(sp.GetRequiredService<AppDataPaths>().DatabasePath));
        services.AddSingleton<IAccountStore, AccountRepository>();
        services.AddSingleton<IFolderStore, FolderRepository>();
        services.AddSingleton<IMessageStore, MessageRepository>();
        services.AddSingleton<IMailSyncService, MailSyncService>();
        services.AddSingleton<IOutboxStore, OutboxRepository>();
        services.AddSingleton<IOutboxProcessor, OutboxProcessor>();
        services.AddSingleton<ISmtpSender, SmtpSender>();
        services.AddSingleton<ISearchIndex, FtsSearchIndex>();
        services.AddSingleton<ISettingsStore, SettingsRepository>();

        // IImapAccountClient wraps one connection, not shared state — resolve a fresh instance
        // per account via this factory rather than injecting the interface directly.
        services.AddTransient<IImapAccountClient, ImapAccountClient>();
        services.AddSingleton<Func<IImapAccountClient>>(sp => () => sp.GetRequiredService<IImapAccountClient>());

        services.AddTransient<MainViewModel>();
        services.AddTransient<AddAccountViewModel>();
        services.AddTransient<SidebarViewModel>();
        services.AddTransient<MessageListViewModel>();
        services.AddTransient<ReadingPaneViewModel>();
        services.AddTransient<ComposeViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<SettingsViewModel>();

        // ICredentialStore / INotificationService are Windows-only; registered by MailClient.App itself.

        return services;
    }
}
