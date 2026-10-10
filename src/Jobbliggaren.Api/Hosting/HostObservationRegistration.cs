using Jobbliggaren.Application.Admin.HostObservations;
using Jobbliggaren.Infrastructure.Diagnostics;

namespace Jobbliggaren.Api.Hosting;

/// <summary>
/// Composes the host sampler into the API and only the API (#1982). The Worker has no admin surface and no
/// reason to read the host on a timer, so this is not in <c>AddPersistence</c> or <c>AddApplication</c>,
/// which both hosts load. Every service is a singleton, with no scoped dependency, so it survives
/// <c>ValidateScopes</c>. Every option has a default: nothing here needs a human-supplied value to boot.
/// </summary>
internal static class HostObservationRegistration
{
    public static IServiceCollection AddHostObservation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HostObservationOptions>()
            .Bind(configuration.GetSection(HostObservationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IHostObservationProbe, ProcFsHostObservationProbe>();
        services.AddSingleton<HostObservationSampler>();
        services.AddSingleton<IHostObservationReader>(sp => sp.GetRequiredService<HostObservationSampler>());
        services.AddSingleton<IHostObservationSampler>(sp => sp.GetRequiredService<HostObservationSampler>());
        services.AddHostedService<HostObservationService>();
        return services;
    }
}
