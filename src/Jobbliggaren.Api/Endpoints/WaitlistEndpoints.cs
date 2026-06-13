using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Waitlist.Commands.RequestWaitlistEntry;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class WaitlistEndpoints
{
    public static void MapWaitlistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/waitlist").WithTags("Waitlist");

        // Anonym signup-endpoint. Kill-switch via IFeatureFlags.RegistrationsOpen
        // blockerar med 503 när stängd. Rate-limit: 3/24h per IP (WaitlistSignupPolicy).
        group.MapPost("/", async (
            RequestWaitlistEntryCommand command,
            IFeatureFlags featureFlags,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (!featureFlags.RegistrationsOpen)
                throw new RegistrationsClosedException();

            var result = await mediator.Send(command, ct);
            if (result.IsFailure)
                return ToErrorResult(result.Error);

            return Results.Ok(new
            {
                waitlistEntryId = result.Value.WaitlistEntryId,
                email = result.Value.Email,
            });
        }).RequireRateLimiting(RateLimitingExtensions.WaitlistSignupPolicy);
    }

    private static IResult ToErrorResult(DomainError error) =>
        Results.Problem(detail: error.Message, title: error.Code, statusCode: 400);
}
