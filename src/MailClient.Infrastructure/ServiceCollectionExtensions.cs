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
        services.AddTransient<MainViewModel>();

        // M1: IAccountStore / IFolderStore / IMessageStore / IOutboxStore -> MailClient.Data repositories
        // M3+: IImapAccountClient / ISmtpSender / IMailSyncService -> MailClient.Mail implementations

        return services;
    }
}
