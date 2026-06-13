using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Invitations;

namespace Jobbliggaren.Domain.Waitlist.Events;

public sealed record WaitlistEntryApprovedDomainEvent(
    WaitlistEntryId WaitlistEntryId,
    Guid ApprovedByAdminId,
    InvitationId ResultingInvitationId,
    DateTimeOffset OccurredAt) : IDomainEvent;
