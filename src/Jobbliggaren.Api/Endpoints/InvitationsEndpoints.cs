using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Invitations.Commands.RedeemInvitation;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class InvitationsEndpoints
{
    public static void MapInvitationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        // Anonym redemption-endpoint. Kill-switch via IFeatureFlags.RegistrationsOpen
        // blockerar med 503 när stängd. Rate-limit: 5/h per IP (InvitationRedeemPolicy).
        // Email kommer från Invitation, INTE från command body (skydd mot token-stöld).
        group.MapPost("/redeem-invitation", async (
            RedeemInvitationCommand command,
            IFeatureFlags featureFlags,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (!featureFlags.RegistrationsOpen)
                throw new RegistrationsClosedException();

            var result = await mediator.Send(command, ct);
            if (result.IsFailure)
                return result.Error.ToProblemResult();

            return Results.Ok(new { sessionId = result.Value.SessionId });
        }).RequireRateLimiting(RateLimitingExtensions.InvitationRedeemPolicy);
    }
}
