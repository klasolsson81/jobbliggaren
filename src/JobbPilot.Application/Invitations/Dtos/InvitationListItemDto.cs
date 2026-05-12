using JobbPilot.Domain.Invitations;

namespace JobbPilot.Application.Invitations.Dtos;

public sealed record InvitationListItemDto(
    Guid InvitationId,
    string Email,
    string Origin,
    string Status,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RedeemedAt,
    DateTimeOffset? RevokedAt)
{
    public static InvitationListItemDto From(Invitation i) =>
        new(i.Id.Value, i.Email, i.Origin.Name, i.Status.Name,
            i.IssuedAt, i.ExpiresAt, i.RedeemedAt, i.RevokedAt);
}
