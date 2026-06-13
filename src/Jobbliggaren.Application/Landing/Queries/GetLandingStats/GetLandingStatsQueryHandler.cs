using Jobbliggaren.Application.Landing.Common;
using Mediator;

namespace Jobbliggaren.Application.Landing.Queries.GetLandingStats;

/// <summary>
/// Returnerar pre-computed stats från <see cref="ILandingStatsCache"/>:
/// <list type="bullet">
///   <item>Cache-hit → returnera värdet (<see cref="LandingStatsDto.IsStale"/>=false satt av Worker).</item>
///   <item>Cache-miss → returnera <see cref="Floor"/> (<see cref="LandingStatsDto.IsStale"/>=true).</item>
/// </list>
/// <para>
/// Handlern träffar ALDRIG databasen synkront — det är Worker:s ansvar
/// (ADR 0064 Variant B). Stampede-fri by design: oavsett hur många
/// parallella requests som råkar komma in vid cache-expiry får alla samma
/// floor-svar tills Worker fyllt nyckeln igen.
/// </para>
/// </summary>
public sealed class GetLandingStatsQueryHandler(ILandingStatsCache cache)
    : IQueryHandler<GetLandingStatsQuery, LandingStatsDto>
{
    // Floor per senior-cto-advisor-dom 2026-05-23 (agentId a1da26dc2029a5def).
    // ActiveCount=40_000 reflekterar post-Punkt-1-retention-korpus; NewToday=0
    // är konservativt — vi ljuger inte uppåt om vi inte vet. IsStale=true
    // talar om för FE (och eventuella partner-integrationer) att värdet inte
    // är aktuellt; UI får besluta om/hur det renderas.
    private static readonly LandingStatsDto Floor = new(
        ActiveCount: 40_000,
        NewToday: 0,
        IsStale: true,
        RefreshedAt: null);

    public async ValueTask<LandingStatsDto> Handle(
        GetLandingStatsQuery query, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(cancellationToken).ConfigureAwait(false);
        return cached ?? Floor;
    }
}
