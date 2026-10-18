using Jobbliggaren.Application.JobAds.Listing;

namespace Jobbliggaren.Api.Hosting;

/// <summary>
/// Composes the listed-ad sampler into the API and only the API (ADR 0174). The Worker never answers the sitemap, so
/// this is not in <c>AddPersistence</c> or <c>AddApplication</c>, which both hosts load: there it would read the whole
/// listed set every five minutes for no reader. Every service is a singleton with no scoped dependency, so it
/// survives <c>ValidateScopes</c>; the sampler opens its own scope per read. Nothing here has an option to set.
/// </summary>
internal static class ListedJobAdSnapshotRegistration
{
    public static IServiceCollection AddListedJobAdSnapshot(this IServiceCollection services)
    {
        services.AddSingleton<ListedJobAdSnapshotSampler>();
        services.AddSingleton<IListedJobAdSnapshotReader>(sp => sp.GetRequiredService<ListedJobAdSnapshotSampler>());
        services.AddSingleton<IListedJobAdSnapshotSampler>(sp => sp.GetRequiredService<ListedJobAdSnapshotSampler>());
        services.AddHostedService<ListedJobAdSnapshotService>();
        return services;
    }
}
