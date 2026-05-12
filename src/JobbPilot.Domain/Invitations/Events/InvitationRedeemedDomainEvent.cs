using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Invitations.Events;

public sealed record InvitationRedeemedDomainEvent(
    InvitationId InvitationId,
    Guid RedeemedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent;
