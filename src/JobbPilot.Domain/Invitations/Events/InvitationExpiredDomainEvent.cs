using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Invitations.Events;

public sealed record InvitationExpiredDomainEvent(
    InvitationId InvitationId,
    DateTimeOffset OccurredAt) : IDomainEvent;
