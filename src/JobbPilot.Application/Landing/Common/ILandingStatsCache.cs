namespace JobbPilot.Application.Landing.Common;

/// <summary>
/// Port för pre-computed landing-stats-cache. ADR 0064 Variant B
/// (Hangfire-cron + Redis). Infrastructure-impl använder
/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
///
/// <para>
/// Worker-jobb <c>RefreshLandingStatsJob</c> skriver var 5:e min via
/// <see cref="SetAsync"/>. Api-endpoint <c>GetLandingStatsQueryHandler</c>
/// läser via <see cref="GetAsync"/>. Endpoint-handlern träffar ALDRIG
/// databasen synkront — det är Worker:s ansvar. Stampede-fri by design.
/// </para>
/// <para>
/// Cache-miss (<see cref="GetAsync"/> returnerar <c>null</c>) betyder
/// antingen att Worker inte har kört än (cold-start-fönster &lt; 5 min) eller
/// att Redis är ner / nyckeln har gått ut. Handler ansvarar för fallback-floor.
/// </para>
/// </summary>
public interface ILandingStatsCache
{
    Task<LandingStatsDto?> GetAsync(CancellationToken cancellationToken);

    Task SetAsync(LandingStatsDto stats, CancellationToken cancellationToken);
}
