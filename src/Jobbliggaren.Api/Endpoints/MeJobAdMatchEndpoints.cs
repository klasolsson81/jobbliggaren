using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;
using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// F4-13 (ADR 0076 Decision 5, ADR 0063) — page-scoped match-tag batch-overlay on the
/// /jobb list. A dedicated batch port (parity <c>POST /me/job-ad-status</c>), kept in its
/// OWN file (SoC) — match is an orthogonal read concern from saved/applied status.
/// `JobAdDto` / `IJobAdSearchQuery` stay untouched (Decision 5).
/// </summary>
public static class MeJobAdMatchEndpoints
{
    public sealed record JobAdMatchBatchRequest(IReadOnlyList<Guid> JobAdIds);

    public static void MapMeJobAdMatchEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous-tolerant (parity the status batch, ADR 0063 §Kontext): the handler
        // returns an empty map without a UserId, and for an authenticated user without a
        // stated occupation — no 401 friction on the public search page. NOT
        // `.RequireAuthorization()`-gated. Own dual-partition rate-limit policy
        // (JobAdMatchBatchPolicy) — a separate bucket from the status batch so the two
        // overlays a page render fires do not share a budget (bulkhead, Nygard); the ip:
        // fallback is the load-bearing DoS guard for the anonymous surface.
        app.MapPost("/api/v1/me/job-ad-match-tags", async (
                JobAdMatchBatchRequest body, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(
                    new GetJobAdMatchBatchQuery(body.JobAdIds ?? []), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireRateLimiting(RateLimitingExtensions.JobAdMatchBatchPolicy);

        // F4-16 (ADR 0076 Amendment (b) §5, ADR 0053 Beslut 5 amendment) — single-ad match
        // detail for the job modal (grade + matched/missing per dimension). UNLIKE the
        // anonymous-tolerant batch overlay above, this is `.RequireAuthorization()`-gated:
        // the modal is auth-gated and the FE only fetches this for a logged-in user (the
        // guest modal renders no match section), so there is no anonymous DoS surface to
        // protect — a user-partitioned read bucket (MeListReadPolicy) suffices, and the
        // DEK-reading handler is never reached anonymously. The handler still returns null
        // for an absent UserId as defence-in-depth. A 200 with `null` body means "no match
        // section" (the FE renders nothing); a missing ad → 404 (NotFoundException).
        app.MapGet("/api/v1/me/job-ad-match-tags/{jobAdId:guid}", async (
                Guid jobAdId, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(new GetJobAdMatchDetailQuery(jobAdId), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);
    }
}
