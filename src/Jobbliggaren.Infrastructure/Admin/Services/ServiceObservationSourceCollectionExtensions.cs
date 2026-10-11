using Jobbliggaren.Application.Admin.ServiceObservations;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Admin.Services;

internal static class ServiceObservationSourceCollectionExtensions
{
    /// <summary>
    /// #1982, ADR 0159. Registered from <c>AddInfrastructure</c>, which only the Api calls: the Worker has no admin
    /// surface and reads no import records. A file of its own so that the next source does not have to touch the
    /// composition root's body. The Hangfire and health-check adapters are in the Api, where those types already live.
    /// </summary>
    public static IServiceCollection AddServiceObservationSources(this IServiceCollection services)
    {
        services.AddSingleton<IImportRunRecords, SqlImportRunRecords>();
        return services;
    }
}
