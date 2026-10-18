using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchCriterionScan;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// ADR 0175 D11 — the Branschbevakning scan's Worker registrations in one place, called by <c>Program.cs</c> and by
/// <c>WorkerTestFixture</c>, so the Worker integration test resolves the wrapper through the registrations the host
/// itself makes. The port the job reads through is registered in <c>AddPersistence</c>.
/// </summary>
public static class CompanyWatchCriterionScanRegistration
{
    public static IServiceCollection AddCompanyWatchCriterionScan(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CompanyWatchCriterionScanOptions>()
            .Bind(configuration.GetSection(CompanyWatchCriterionScanOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<CompanyWatchCriterionScanJob>();
        services.AddScoped<CompanyWatchCriterionScanWorker>();
        return services;
    }
}
