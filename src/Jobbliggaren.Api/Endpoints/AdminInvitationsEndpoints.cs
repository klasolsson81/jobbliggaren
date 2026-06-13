using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Invitations.Commands.IssueInvitation;
using Jobbliggaren.Application.Invitations.Commands.RevokeInvitation;
using Jobbliggaren.Application.Invitations.Queries.ListInvitations;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Api.Endpoints;

public static class AdminInvitationsEndpoints
{
    public static void MapAdminInvitationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/invitations")
            .WithTags("Admin/Invitations")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapPost("/", async (
            IssueInvitationCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/admin/invitations/{result.Value.InvitationId}", result.Value)
                : ToErrorResult(result.Error);
        });

        group.MapGet("/", async (
            IMediator mediator,
            string? status = null,
            CancellationToken ct = default) =>
        {
            var items = await mediator.Send(new ListInvitationsQuery(status), ct);
            return Results.Ok(items);
        });

        group.MapPost("/{id:guid}/revoke", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RevokeInvitationCommand(id), ct);
            return result.IsSuccess ? Results.NoContent() : ToErrorResult(result.Error);
        });
    }

    private static IResult ToErrorResult(DomainError error) => error.Code switch
    {
        "Invitation.NotFound" => Results.Problem(detail: error.Message, title: error.Code, statusCode: 404),
        "Invitation.NotPending" => Results.Problem(detail: error.Message, title: error.Code, statusCode: 409),
        _ => Results.Problem(detail: error.Message, title: error.Code, statusCode: 400),
    };
}
