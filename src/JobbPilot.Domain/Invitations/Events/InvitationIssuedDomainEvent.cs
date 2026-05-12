using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Invitations.Events;

public sealed record InvitationIssuedDomainEvent(
    InvitationId InvitationId,
    string Email,
    InvitationOrigin Origin,
    Guid IssuedByAdminId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset OccurredAt) : IDomainEvent;
