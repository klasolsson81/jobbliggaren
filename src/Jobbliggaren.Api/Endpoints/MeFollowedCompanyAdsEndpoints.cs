using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.CompanyWatches.Commands.SetLastSeenFollowedAds;
using Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B) — the in-app follow-rail surface on Översikt: the count of new ads
/// from followed employers since the user's last visit, and the watermark-advance when they visit
/// the follows hub. Route prefix <c>/api/v1/me/followed-company-ads</c> ("my data" convention); the
/// whole group is auth-gated (a per-user watermark — anon has none).
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
    /// empty body (the follows hub renders no individual hits to preserve / deploy-skew) is allowed
    /// and falls back to clock-now in the handler (parity <c>MarkMatchesSeenRequest</c>).
    /// </summary>
    internal sealed record SetLastSeenFollowedAdsRequest(DateTimeOffset? SeenThrough);

    public static void MapMeFollowedCompanyAdsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/followed-company-ads")
            .WithTags("Me")
            .RequireAuthorization();

        // Översikts follow-rail count ("nya annonser från bevakade företag" NEW since the last visit).
        // Auth-gated; a per-user grade-filtered count over the user's OWN hits + active watches (no
        // cross-user surface, no org.nr). MeListReadPolicy (parity /me/new-match-count). 200 { count }.
        group.MapGet("/new-count", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetNewFollowedCompanyAdCountQuery(), ct);
            return Results.Ok(result);
        }).RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // Advance the follow rail watermark (reset the count) — called when the user visits the
        // follows hub (/foretag). Body { seenThrough } nullable → handler clock-now fallback (#477
        // posture). Auth-gated, MeWritePolicy (parity /me/matches/seen). 204 / 400.
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
