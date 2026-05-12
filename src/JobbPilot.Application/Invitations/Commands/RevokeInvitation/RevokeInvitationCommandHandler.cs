using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.Invitations;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Invitations.Commands.RevokeInvitation;

public sealed class RevokeInvitationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<RevokeInvitationCommand, Result>
{
    public async ValueTask<Result> Handle(
        RevokeInvitationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
            return Result.Failure(
                DomainError.Validation("Invitation.AdminUnknown", "Admin-användaren kunde inte identifieras."));

        var id = new InvitationId(command.InvitationId);
        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (invitation is null)
            return Result.Failure(DomainError.NotFound("Invitation", command.InvitationId));

        return invitation.Revoke(currentUser.UserId.Value, clock);
    }
}
