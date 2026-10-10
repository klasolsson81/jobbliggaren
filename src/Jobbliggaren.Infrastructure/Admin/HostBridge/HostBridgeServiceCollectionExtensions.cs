using Jobbliggaren.Application.Admin.Backup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Admin.HostBridge;

internal static class HostBridgeServiceCollectionExtensions
{
    /// <summary>
    /// #1982, ADR 0157. Registered from <c>AddInfrastructure</c>, which only the Api calls: the Worker reads
    /// nothing from the host bridge. A file of its own so that the next source (which would add a reader
    /// here) does not have to touch the composition root's body.
    /// </summary>
    public static IServiceCollection AddHostBridge(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HostBridgeOptions>().Bind(configuration.GetSection(HostBridgeOptions.SectionName));
        services.AddSingleton<HostBridgeFileReader>();
        services.AddSingleton<IBackupSampleSource, BackupSampleSource>();
        return services;
    }
}
