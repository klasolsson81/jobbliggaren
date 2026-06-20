using Jobbliggaren.Application.Dev.Commands.ResetMyData;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// DEV-ONLY endpoints — NOT mapped in production; remove before launch (Klas).
/// These exist solely so Klas can re-test onboarding flows locally. The caller
/// (<c>Program.cs</c>) MUST guard the registration with
/// <c>app.Environment.IsDevelopment()</c> so the routes never exist in a deployed
/// environment (defense in depth alongside the FE button being dev-gated too).
/// </summary>
public static class DevEndpoints
{
    public static void MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dev").WithTags("Dev");

        // DEV-ONLY — clears the current user's CV data, saved/recent searches and
        // resets match preferences to Empty (re-triggers the welcome modal). Does
        // NOT delete the account — the login keeps working. Owner-scoped inside the
        // handler (ICurrentUser → JobSeeker). Returns 204. REMOVE BEFORE LAUNCH.
        group.MapPost("/reset-my-data", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ResetMyDataCommand(), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(
                    detail: result.Error.Message,
                    title: result.Error.Code,
                    statusCode: 400);
        }).RequireAuthorization();
    }
}
