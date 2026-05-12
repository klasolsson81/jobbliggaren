using JobbPilot.Api.RateLimiting;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Application.Invitations.Commands.RedeemInvitation;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Api.Endpoints;

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
                return ToErrorResult(result.Error);

            return Results.Ok(new { sessionId = result.Value.SessionId });
        }).RequireRateLimiting(RateLimitingExtensions.InvitationRedeemPolicy);
    }

    private static IResult ToErrorResult(DomainError error) => error.Code switch
    {
        "Invitation.NotFound" => Results.Problem(detail: error.Message, title: error.Code, statusCode: 404),
        "Invitation.Expired" or "Invitation.Revoked" or "Invitation.AlreadyRedeemed"
            => Results.Problem(detail: error.Message, title: error.Code, statusCode: 410),
        _ => Results.Problem(detail: error.Message, title: error.Code, statusCode: 400),
    };
}
