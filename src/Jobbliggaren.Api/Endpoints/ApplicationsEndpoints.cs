using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Applications.Commands.AddFollowUp;
using Jobbliggaren.Application.Applications.Commands.AddNote;
using Jobbliggaren.Application.Applications.Commands.AttachResumeVersion;
using Jobbliggaren.Application.Applications.Commands.CreateApplication;
using Jobbliggaren.Application.Applications.Commands.CreateApplicationFromJobAd;
using Jobbliggaren.Application.Applications.Commands.LogFollowUp;
using Jobbliggaren.Application.Applications.Commands.RecordFollowUpOutcome;
using Jobbliggaren.Application.Applications.Commands.TransitionTo;
using Jobbliggaren.Application.Applications.Queries.GetActivityReport;
using Jobbliggaren.Application.Applications.Queries.GetApplicationById;
using Jobbliggaren.Application.Applications.Queries.GetApplications;
using Jobbliggaren.Application.Applications.Queries.GetApplicationStats;
using Jobbliggaren.Application.Applications.Queries.GetPipeline;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class ApplicationsEndpoints
{
    public static void MapApplicationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/applications").WithTags("Applications");

        group.MapGet("/", async (
            IMediator mediator,
            int page = 1,
            int pageSize = 20,
            string? status = null,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new GetApplicationsQuery(page, pageSize, status), ct);
            return Results.Ok(result);
        }).RequireAuthorization();

        group.MapGet("/pipeline", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetPipelineQuery(), ct);
            return Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // #316 — AF activity-report helper. Owner-scoped read; year/month
        // optional (absent → previous month, computed server-side). Validation
        // pipeline returns 400 on a malformed pair.
        group.MapGet("/activity-report", async (
            IMediator mediator,
            int? year = null,
            int? month = null,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new GetActivityReportQuery(year, month), ct);
            return Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // #313 — application statistics (BUILD.md §6.2: avslags-analys, pipeline-
        // konvertering). Owner-scoped read aggregate; no parameters (fixed metric
        // set + rolling 12-month window). Same read rate-limit policy as the other
        // list/aggregate reads.
        group.MapGet("/stats", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetApplicationStatsQuery(), ct);
            return Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetApplicationByIdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization();

        group.MapPost("/", async (
            CreateApplicationCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/applications/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        // F6 P5 Punkt 2 Del B — "Har ansökt"-quick-create från jobbmodal-footer.
        // Separat endpoint per CTO Val 3 (SRP: olika preconditions, olika
        // optimistic-UI). Path är /{jobAdId} (semantisk nyckel, paritet med
        // /api/v1/me/saved-job-ads/{jobAdId}).
        group.MapPost("/from-job-ad/{jobAdId:guid}", async (
            Guid jobAdId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new CreateApplicationFromJobAdCommand(jobAdId), ct);
            return result.IsSuccess
                ? Results.Created(
                    $"/api/v1/applications/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/transition", async (
            Guid id, TransitionToBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new TransitionToCommand(id, body.TargetStatus), ct);
            return result.IsSuccess
                ? Results.Ok()
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/follow-ups", async (
            Guid id, AddFollowUpBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new AddFollowUpCommand(id, body.Channel, body.ScheduledAt, body.Note), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/applications/{id}/follow-ups/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        // ADR 0092 D4/D5 — "Logga uppföljning": log a completed contact today (note
        // optional). Distinct from the scheduled follow-up above; the aggregate
        // defaults channel=Other/scheduled=now/outcome=Logged and bumps LastFollowUpAt
        // so the effective wait resets. The literal "log" segment never collides with
        // the {followUpId:guid}-constrained outcome route below.
        group.MapPost("/{id:guid}/follow-ups/log", async (
            Guid id, LogFollowUpBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new LogFollowUpCommand(id, body.Note), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/applications/{id}/follow-ups/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/follow-ups/{followUpId:guid}/outcome", async (
            Guid id, Guid followUpId, RecordFollowUpOutcomeBody body,
            IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new RecordFollowUpOutcomeCommand(id, followUpId, body.Outcome), ct);
            return result.IsSuccess
                ? Results.Ok()
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/notes", async (
            Guid id, AddNoteBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new AddNoteCommand(id, body.Content), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/applications/{id}/notes/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        // F4-11: link the exact ResumeVersion (Master/Tailored) used for this application
        // (BUILD §5.3). Owner-scoped, IDOR fail-closed — the version must belong to the caller's
        // OWN Resume; a cross-user or unknown application/version returns an identical 404 (no
        // enumeration oracle) + a cross-user audit. Replaceable while the application is
        // non-terminal. 204 on success.
        group.MapPost("/{id:guid}/resume-version", async (
            Guid id, AttachResumeVersionBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new AttachResumeVersionCommand(id, body.ResumeVersionId), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }

    private sealed record TransitionToBody(string TargetStatus);
    private sealed record AddFollowUpBody(string Channel, DateTimeOffset ScheduledAt, string? Note);
    private sealed record LogFollowUpBody(string? Note);
    private sealed record AddNoteBody(string? Content);
    private sealed record RecordFollowUpOutcomeBody(string Outcome);
    private sealed record AttachResumeVersionBody(Guid ResumeVersionId);
}
