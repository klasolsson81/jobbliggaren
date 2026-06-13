using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Invitations.Events;

public sealed record InvitationRedeemedDomainEvent(
    InvitationId InvitationId,
    Guid RedeemedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent;
