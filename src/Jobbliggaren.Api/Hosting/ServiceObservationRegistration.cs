using Jobbliggaren.Api.BackgroundJobs;
using Jobbliggaren.Api.HealthChecks;
using Jobbliggaren.Application.Admin.ServiceObservations;

namespace Jobbliggaren.Api.Hosting;

/// <summary>
/// Composes the services sampler into the API and only the API (#1982, ADR 0159). The Worker has no admin surface
/// and must not watch itself, so this is not in <c>AddPersistence</c> or <c>AddApplication</c>, which both hosts
/// load. Every service is a singleton with no scoped dependency, so it survives <c>ValidateScopes</c>; the SQL
/// source registered by <c>AddInfrastructure</c> opens its own scope. Every option has a default: nothing here
/// needs a human-supplied value to boot.
/// </summary>
internal static class ServiceObservationRegistration
{
    public static IServiceCollection AddServiceObservation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ServiceObservationOptions>()
            .Bind(configuration.GetSection(ServiceObservationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IWorkerHeartbeatSource, HangfireWorkerHeartbeatSource>();
        services.AddSingleton<IVolatileStoreProbe, ReadinessVolatileStoreProbe>();
        services.AddSingleton<ServiceObservationSampler>();
        services.AddSingleton<IServiceObservationReader>(sp => sp.GetRequiredService<ServiceObservationSampler>());
        services.AddSingleton<IServiceObservationSampler>(sp => sp.GetRequiredService<ServiceObservationSampler>());
        services.AddHostedService<ServiceObservationService>();
        return services;
    }
}
