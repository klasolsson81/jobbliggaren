using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Invitations.Events;

public sealed record InvitationRevokedDomainEvent(
    InvitationId InvitationId,
    Guid RevokedByAdminId,
    DateTimeOffset OccurredAt) : IDomainEvent;
