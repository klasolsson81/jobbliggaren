using Jobbliggaren.Application.Admin.Email.Dmarc;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — the stored DMARC reports' registrations, split by host: the Worker ingests and deletes,
/// the Api only reads. Each needs the <c>AppDbContext</c> that <c>AddPersistence</c> registers.
/// </summary>
public static class DmarcReportPersistenceCollectionExtensions
{
    /// <summary>The Worker's side: the ingestion store and the retention.</summary>
    public static IServiceCollection AddDmarcReportIngestionStores(this IServiceCollection services)
    {
        services.AddScoped<IDmarcReportStore, SqlDmarcReportStore>();
        services.AddScoped<IDmarcReportRetention, SqlDmarcReportRetention>();
        return services;
    }

    /// <summary>The Api's side: the page's read.</summary>
    public static IServiceCollection AddDmarcReportsSource(this IServiceCollection services)
    {
        services.AddScoped<IDmarcReportsSource, SqlDmarcReportsSource>();
        return services;
    }
}
