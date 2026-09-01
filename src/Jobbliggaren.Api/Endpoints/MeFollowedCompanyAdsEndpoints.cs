using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.CompanyWatches.Commands.SetLastSeenFollowedAds;
using Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewFollowedCompanyAds;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B) — the in-app follow-rail surface on Översikt: the count of new ads
/// from followed employers since the user last acknowledged the rail, the ads behind that count, and
/// the watermark-advance that destination performs (#1576). Route prefix
/// <c>/api/v1/me/followed-company-ads</c> ("my data" convention); the whole group is auth-gated (a per-user watermark — anon has none).
///
/// <para>
/// Kept in its OWN file (SoC, parity <c>MeJobsEndpoints</c>/<c>MeJobAdMatchEndpoints</c>) — this is
/// the follow-RAIL read/watermark concern, distinct from <c>CompanyWatchesEndpoints</c> (follow/
/// unfollow CRUD) and its per-hit <c>/ad-hits/{jobAdId}/seen</c> email-dedup stamp (#453). The
/// count DTO carries NO org.nr and no company name (D8 — "rätt säkert läge").
/// </para>
/// </summary>
public static class MeFollowedCompanyAdsEndpoints
{
    /// <summary>
    /// Body for <c>POST /api/v1/me/followed-company-ads/seen</c>. <c>SeenThrough</c> nullable so an
    /// empty body (deploy-skew) is allowed and falls back to clock-now in the handler (parity <c>MarkMatchesSeenRequest</c>).
    /// </summary>
    internal sealed record SetLastSeenFollowedAdsRequest(DateTimeOffset? SeenThrough);

    public static void MapMeFollowedCompanyAdsEndpoints(this IEndpointRouteBuilder app)
    {
        // private, no-store on both reads: per-user by construction, and since #1576 the payload is a
        // list of ads plus a profiling flag rather than an int. Three layers already close an accidental
        // cache (the API is not edge-exposed, authedFetch forces no-store in a type that cannot be
        // overridden, and RFC 9111 forbids a shared cache storing an Authorization response), so this is
        // defence in depth rather than the only control (security-auditor 2026-08-31).
        var group = app.MapGroup("/api/v1/me/followed-company-ads")
            .WithTags("Me")
            .RequireAuthorization();

        // Översikts follow-rail count ("nya annonser från bevakade företag" NEW since the last visit).
        // Auth-gated; a per-user grade-filtered count over the user's OWN hits + active watches (no
        // cross-user surface, no org.nr). MeListReadPolicy (parity /me/new-match-count). 200 { count }.
        group.MapGet("/new-count", async (HttpContext http, IMediator mediator, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new GetNewFollowedCompanyAdCountQuery(), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // #1576 — the ads BEHIND that count: the destination the Översikt number links to. Same
        // definition, same predicate (NewFollowedCompanyAdSet), so the number and this set cannot
        // disagree. Parameterless: the "bara de som matchar"-arm is a view filter over rows that
        // already carry their own MatchesYou, never a second query. Capped, not paginated — the
        // response carries the acknowledgement window, which PagedResult has nowhere to put.
        // MeListReadPolicy (parity /new-count). 200 { rows, acknowledgedThrough, truncated }.
        group.MapGet("/", async (HttpContext http, IMediator mediator, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var result = await mediator.Send(new ListNewFollowedCompanyAdsQuery(), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // Advance the follow rail watermark (reset the count) — called by /foretag/bevakade/nya once it
        // has rendered the window it acknowledges (#1576). Body { seenThrough } nullable → handler
        // clock-now fallback (#477 posture). Auth-gated, MeWritePolicy (parity /me/matches/seen). 204 / 400.
        group.MapPost("/seen", async (
            SetLastSeenFollowedAdsRequest? body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new SetLastSeenFollowedAdsCommand(body?.SeenThrough), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }
}
