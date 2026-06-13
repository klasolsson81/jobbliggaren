using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Invitations.Events;

public sealed record InvitationExpiredDomainEvent(
    InvitationId InvitationId,
    DateTimeOffset OccurredAt) : IDomainEvent;
