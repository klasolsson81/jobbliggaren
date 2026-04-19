using JobbPilot.Application.Auth.Queries.GetCurrentUser;
using JobbPilot.Application.JobSeekers.Commands.UpdateMyProfile;
using JobbPilot.Application.JobSeekers.Queries.GetMyProfile;
using Mediator;

namespace JobbPilot.Api.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me").WithTags("Me");

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetCurrentUserQuery(), ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireAuthorization();

        group.MapGet("/profile", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetMyProfileQuery(), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization();

        group.MapPatch("/profile", async (
            UpdateMyProfileCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Ok()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();
    }
}
