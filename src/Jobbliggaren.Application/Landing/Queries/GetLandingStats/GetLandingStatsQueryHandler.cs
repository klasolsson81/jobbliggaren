using Jobbliggaren.Application.Landing.Common;
using Mediator;

namespace Jobbliggaren.Application.Landing.Queries.GetLandingStats;

/// <summary>
/// Returnerar pre-computed stats från <see cref="ILandingStatsCache"/>:
/// <list type="bullet">
///   <item>Cache-hit → returnera värdet (<see cref="LandingStatsDto.IsStale"/>=false satt av Worker).</item>
///   <item>Cache-miss → returnera <see cref="LandingStatsDto.Unknown"/> (talen är <c>null</c>).</item>
/// </list>
///
/// <para>
/// Handlern träffar ALDRIG databasen synkront — det är Worker:s ansvar (ADR 0064 Variant B).
/// Stampede-fri by design: oavsett hur många parallella requests som råkar komma in vid cache-expiry
/// får alla samma svar utan en enda DB-rundtur. <b>Den arkitekturen står orörd.</b>
/// </para>
///
/// <para>
/// <b>Vad som ändrades (CTO-bind 2026-07-13, A′): cache-miss returnerar inte längre ett hårdkodat
/// GOLV.</b> Golvet (<c>ActiveCount: 40_000</c>) var en siffra ingen mätt, och landningssidan renderade
/// den som ett faktum. Att svara "vi vet inte" är billigt och sant; att svara med ett påhittat tal är
/// varken. Se <see cref="LandingStatsDto"/> för hela motiveringen — och notera att en MÄTT nolla
/// fortfarande är <c>0</c>, aldrig <c>null</c>.
/// </para>
/// </summary>
public sealed class GetLandingStatsQueryHandler(ILandingStatsCache cache)
    : IQueryHandler<GetLandingStatsQuery, LandingStatsDto>
{
    public async ValueTask<LandingStatsDto> Handle(
        GetLandingStatsQuery query, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(cancellationToken).ConfigureAwait(false);
        return cached ?? LandingStatsDto.Unknown;
    }
}
