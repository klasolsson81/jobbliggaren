using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobAds.Commands.MarkJobsSeen;
using Jobbliggaren.Application.JobAds.Queries.GetJobsWatermark;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — per-user "Ny = arrived since your last visit"
/// watermark for the /jobb surface. The sibling of the matches-surface watermark endpoints
/// (<see cref="MeJobAdMatchEndpoints"/> POST /me/matches/seen + GET /me/new-match-count).
/// Two endpoints, both auth-gated (the watermark is per-user; anon has none):
/// <list type="bullet">
/// <item><c>GET  /api/v1/me/jobs/watermark</c> — read the scalar last-seen-jobs timestamp;
///   the FE computes the per-user "Ny" tag client-side (NY = JobAd.CreatedAt &gt; watermark),
///   keeping per-user state OFF the public JobAdDto projection (ADR 0063 Beslut b).</item>
/// <item><c>POST /api/v1/me/jobs/seen</c> — advance the watermark to now (called on /jobb load,
///   AFTER the list is read — fetch-then-mark, so this visit still shows new-since-last-visit).</item>
/// </list>
/// </summary>
public static class MeJobsEndpoints
{
    public static void MapMeJobsEndpoints(this IEndpointRouteBuilder app)
    {
        // Read the user-read watermark. Auth-gated: anon has no per-user watermark and the
        // handler returns null (FE then renders no NY for anon). MeListRead (per-user read bucket).
        app.MapGet("/api/v1/me/jobs/watermark", async (
                IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetJobsWatermarkQuery(), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // Advance the watermark (mark the /jobb list seen through the viewed window). Owner-scoped
        // mutation, MeWrite (parity POST /me/matches/seen). Body { seenThrough } = max CreatedAt of
        // the ads the FE actually rendered (#759, sibling of #477 Low 4 — the watermark is set to
        // this, not clock-now). Nullable/empty body (empty list / deploy-skew from an older FE) →
        // handler falls back to clock-now. Monotonic in the aggregate. 204 / 400.
        app.MapPost("/api/v1/me/jobs/seen", async (
                MarkJobsSeenRequest? body, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new MarkJobsSeenCommand(body?.SeenThrough), ct);
                return result.IsSuccess
                    ? Results.NoContent()
                    : result.Error.ToProblemResult();
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }
}

/// <summary>
/// Body for <c>POST /api/v1/me/jobs/seen</c> (#759, sibling of #477 Low 4). <c>SeenThrough</c> =
/// the max <c>CreatedAt</c> of the ads the FE actually rendered on the loaded page; nullable so an
/// empty body (empty list / deploy-skew) is allowed and falls back to clock-now in the handler.
/// </summary>
internal sealed record MarkJobsSeenRequest(DateTimeOffset? SeenThrough);
