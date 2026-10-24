using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportIngestion;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportRetention;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the DMARC report ingestion and retention in one registration, which <c>Program.cs</c> and the
/// Worker test fixture both call. Only the Worker references the mailbox project, so only this host can build the mailbox.
/// The jobs are always registered: without a password the ingestion records that the mailbox is not configured.
/// </summary>
public static class DmarcReportIngestionRegistration
{
    public static IServiceCollection AddDmarcReportIngestion(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDmarcMailbox(configuration);
        services.AddDmarcReportIngestionStores();
        services.AddScoped<DmarcReportIngestionJob>();
        services.AddScoped<DmarcReportRetentionJob>();
        services.AddScoped<DmarcReportIngestionWorker>();
        services.AddScoped<DmarcReportRetentionWorker>();
        return services;
    }
}
