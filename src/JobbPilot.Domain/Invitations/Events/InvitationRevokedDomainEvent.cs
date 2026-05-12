using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Invitations.Events;

public sealed record InvitationRevokedDomainEvent(
    InvitationId InvitationId,
    Guid RevokedByAdminId,
    DateTimeOffset OccurredAt) : IDomainEvent;
