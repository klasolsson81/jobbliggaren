using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.UserStatus.Queries.GetJobAdStatusBatch;
using Jobbliggaren.Application.UserStatus.Queries.HasApplied;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// ADR 0063 — per-user-overlay-status på publika JobAd-listor.
/// Två endpoints:
/// - `POST /api/v1/me/job-ad-status` (batch — list-yta, max 100 IDs)
/// - `GET  /api/v1/me/applications/has-applied/{jobAdId}` (single — modal-yta)
/// </summary>
public static class MeJobAdStatusEndpoints
{
    public sealed record JobAdStatusBatchRequest(IReadOnlyList<Guid> JobAdIds);

    public static void MapMeJobAdStatusEndpoints(this IEndpointRouteBuilder app)
    {
        // ADR 0063 §Kontext + CTO-dom 2026-05-23 (Minor 9 Variant A) —
        // anonym-tolerant; handler returnerar tom DTO utan UserId. Endpoint
        // INTE `.RequireAuthorization()`-gated (skiljs från modal-single nedan).
        // Rate-limit per anonym IP + per user lyfts som TD-87 (fas-konsistent
        // batch med Saved/Recent-endpoints innan F6 P5 Punkt 2-fas-stängning).
        app.MapPost("/api/v1/me/job-ad-status", async (
                JobAdStatusBatchRequest body, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(
                    new GetJobAdStatusBatchQuery(body.JobAdIds ?? []), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireRateLimiting(RateLimitingExtensions.JobAdStatusBatchPolicy);

        app.MapGet("/api/v1/me/applications/has-applied/{jobAdId:guid}", async (
                Guid jobAdId, IMediator mediator, CancellationToken ct) =>
            {
                var hasApplied = await mediator.Send(new HasAppliedQuery(jobAdId), ct);
                return Results.Ok(new { hasApplied });
            })
            .WithTags("Me")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);
    }
}
