using JobbPilot.Application.UserStatus.Queries.GetJobAdStatusBatch;
using JobbPilot.Application.UserStatus.Queries.HasApplied;
using Mediator;

namespace JobbPilot.Api.Endpoints;

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
        app.MapPost("/api/v1/me/job-ad-status", async (
                JobAdStatusBatchRequest body, IMediator mediator, CancellationToken ct) =>
            {
                var result = await mediator.Send(
                    new GetJobAdStatusBatchQuery(body.JobAdIds ?? []), ct);
                return Results.Ok(result);
            })
            .WithTags("Me")
            .RequireAuthorization();

        app.MapGet("/api/v1/me/applications/has-applied/{jobAdId:guid}", async (
                Guid jobAdId, IMediator mediator, CancellationToken ct) =>
            {
                var hasApplied = await mediator.Send(new HasAppliedQuery(jobAdId), ct);
                return Results.Ok(new { hasApplied });
            })
            .WithTags("Me")
            .RequireAuthorization();
    }
}
