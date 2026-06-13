using Jobbliggaren.Application.Landing.Common;
using Mediator;

namespace Jobbliggaren.Application.Landing.Queries.GetLandingStats;

/// <summary>
/// Anonym publik query — hämtar pre-computed landing-stats från Redis-cache.
/// ADR 0064. Hot-path per ADR 0045 Beslut 1 klass (a) read-query/list
/// (p95 ≤ 300 ms). Handler-implementationen träffar aldrig databasen direkt;
/// cache-miss returnerar fallback-floor.
/// </summary>
public sealed record GetLandingStatsQuery : IQuery<LandingStatsDto>;
