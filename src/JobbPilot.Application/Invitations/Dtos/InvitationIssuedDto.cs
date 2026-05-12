using JobbPilot.Domain.Invitations;

namespace JobbPilot.Application.Invitations.Dtos;

public sealed record InvitationIssuedDto(Guid InvitationId, string Email, DateTimeOffset ExpiresAt)
{
    public static InvitationIssuedDto From(Invitation invitation) =>
        new(invitation.Id.Value, invitation.Email, invitation.ExpiresAt);
}
