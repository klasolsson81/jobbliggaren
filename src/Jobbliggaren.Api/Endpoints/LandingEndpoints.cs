using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Landing.Queries.GetLandingStats;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// Publik anonym landing-stats-endpoint. ADR 0064 — pre-computed Redis-cache
/// via Worker-jobb (<c>RefreshLandingStatsJob</c>). Endpoint = ren cache-
/// läsning, stampede-fri by design.
///
/// <para>
/// Ingen <c>.RequireAuthorization()</c> — publik landingpage anropas av
/// anonyma besökare. Rate-limit via <see cref="RateLimitingExtensions.LandingPublicReadPolicy"/>
/// (IP-partitionerad, fixed-window 60/min default per senior-cto-advisor-dom
/// 2026-05-23). DoS-yta minimerad via short-circuit cache-hit (Redis GET
/// sub-10ms) + bypass av all DB-IO.
/// </para>
/// <para>
/// Hot-path per ADR 0045 Beslut 1 klass (a) read-query/list (p95 ≤ 300 ms,
/// Klas-låst). Realistic mätning: Redis GET över ECS-internnät ger
/// deterministisk sub-10ms latens.
/// </para>
/// </summary>
public static class LandingEndpoints
{
    public static void MapLandingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/landing").WithTags("Landing");

        group.MapGet("/stats", async (IMediator mediator, HttpContext http, CancellationToken ct) =>
            {
                var stats = await mediator.Send(new GetLandingStatsQuery(), ct);

                // security-auditor 2026-05-23 Min-1 (agentId a5d1509436995d094):
                // public-cache-header för CDN/proxy/BFF. 30s public-cache är
                // strikt mindre än Worker:s 5-min refresh-fönster — frontend
                // upplever värdena som live. Vid CloudFront/proxy framöver
                // absorberar de större delen av trafiken så DoS-yta minskar.
                //
                // MEN: ett OMÄTT svar får aldrig pinnas i en delad cache (CTO-bind 2026-07-13, A′).
                // Workern warm-startar cachen vid boot och fyller den på sekunder; en CDN som cachat
                // "vi vet inte" i 30 s skulle sträcka ut det okända fönstret igen — för alla besökare —
                // och därmed underminera precis den warm-start samma ändring inför.
                http.Response.Headers.CacheControl = stats.IsStale
                    ? "no-store"
                    : "public, max-age=30";

                return Results.Ok(stats);
            })
            .RequireRateLimiting(RateLimitingExtensions.LandingPublicReadPolicy);
    }
}
