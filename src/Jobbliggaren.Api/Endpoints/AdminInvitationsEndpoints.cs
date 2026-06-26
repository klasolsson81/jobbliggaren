using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Invitations.Commands.IssueInvitation;
using Jobbliggaren.Application.Invitations.Commands.RevokeInvitation;
using Jobbliggaren.Application.Invitations.Queries.ListInvitations;
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
                : result.Error.ToProblemResult();
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
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemResult();
        });
    }
}
