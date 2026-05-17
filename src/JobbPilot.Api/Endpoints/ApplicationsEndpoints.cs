using JobbPilot.Application.Applications.Commands.AddFollowUp;
using JobbPilot.Application.Applications.Commands.AddNote;
using JobbPilot.Application.Applications.Commands.CreateApplication;
using JobbPilot.Application.Applications.Commands.RecordFollowUpOutcome;
using JobbPilot.Application.Applications.Commands.TransitionTo;
using JobbPilot.Application.Applications.Queries.GetApplicationById;
using JobbPilot.Application.Applications.Queries.GetApplications;
using JobbPilot.Application.Applications.Queries.GetPipeline;
using Mediator;

namespace JobbPilot.Api.Endpoints;

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
        }).RequireAuthorization();

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
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/transition", async (
            Guid id, TransitionToBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new TransitionToCommand(id, body.TargetStatus), ct);
            return result.IsSuccess
                ? Results.Ok()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/follow-ups", async (
            Guid id, AddFollowUpBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new AddFollowUpCommand(id, body.Channel, body.ScheduledAt, body.Note), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/applications/{id}/follow-ups/{result.Value}", new { id = result.Value })
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/follow-ups/{followUpId:guid}/outcome", async (
            Guid id, Guid followUpId, RecordFollowUpOutcomeBody body,
            IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new RecordFollowUpOutcomeCommand(id, followUpId, body.Outcome), ct);
            return result.IsSuccess
                ? Results.Ok()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/notes", async (
            Guid id, AddNoteBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new AddNoteCommand(id, body.Content), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/applications/{id}/notes/{result.Value}", new { id = result.Value })
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();
    }

    private sealed record TransitionToBody(string TargetStatus);
    private sealed record AddFollowUpBody(string Channel, DateTimeOffset ScheduledAt, string? Note);
    private sealed record AddNoteBody(string? Content);
    private sealed record RecordFollowUpOutcomeBody(string Outcome);
}
