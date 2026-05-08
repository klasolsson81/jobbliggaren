using JobbPilot.Application.Resumes.Commands.CreateResume;
using JobbPilot.Application.Resumes.Commands.DeleteResume;
using JobbPilot.Application.Resumes.Commands.DeleteResumeVersion;
using JobbPilot.Application.Resumes.Commands.RenameResume;
using JobbPilot.Application.Resumes.Commands.UpdateMasterContent;
using JobbPilot.Application.Resumes.Queries;
using JobbPilot.Application.Resumes.Queries.GetResumeById;
using JobbPilot.Application.Resumes.Queries.GetResumes;
using Mediator;

namespace JobbPilot.Api.Endpoints;

public static class ResumesEndpoints
{
    public static void MapResumesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/resumes").WithTags("Resumes");

        group.MapGet("/", async (
            IMediator mediator,
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new GetResumesQuery(page, pageSize), ct);
            return Results.Ok(result);
        }).RequireAuthorization();

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetResumeByIdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization();

        group.MapPost("/", async (
            CreateResumeBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateResumeCommand(body.Name, body.FullName), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/resumes/{result.Value}", new { id = result.Value })
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPatch("/{id:guid}", async (
            Guid id, RenameResumeBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RenameResumeCommand(id, body.Name), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPut("/{id:guid}/master", async (
            Guid id, ResumeContentDto content, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UpdateMasterContentCommand(id, content), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapDelete("/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteResumeCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapDelete("/{id:guid}/versions/{versionId:guid}", async (
            Guid id, Guid versionId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteResumeVersionCommand(id, versionId), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();
    }

    private sealed record CreateResumeBody(string Name, string FullName);
    private sealed record RenameResumeBody(string Name);
}
