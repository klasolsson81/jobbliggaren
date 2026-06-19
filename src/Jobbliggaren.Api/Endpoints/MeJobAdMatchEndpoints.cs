using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;
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
    }
}
