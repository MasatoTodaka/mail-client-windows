using MailClient.Core.Abstractions;
using MailClient.Data;
using MailClient.Data.Repositories;
using MailClient.ViewModels.AccountSetup;
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
            return new MailDbContext(Path.Combine(dataDir, "mailclient.db"));
        });
        services.AddSingleton<IAccountStore, AccountRepository>();

        services.AddTransient<MainViewModel>();
        services.AddTransient<AddAccountViewModel>();

        // M3+: IFolderStore / IMessageStore / IOutboxStore -> MailClient.Data repositories
        // M3+: IImapAccountClient / ISmtpSender / IMailSyncService -> MailClient.Mail implementations
        // ICredentialStore / INotificationService are Windows-only; registered by MailClient.App itself.

        return services;
    }
}
