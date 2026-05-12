using JobbPilot.Domain.Common;
using JobbPilot.Domain.Invitations;

namespace JobbPilot.Domain.Waitlist.Events;

public sealed record WaitlistEntryApprovedDomainEvent(
    WaitlistEntryId WaitlistEntryId,
    Guid ApprovedByAdminId,
    InvitationId ResultingInvitationId,
    DateTimeOffset OccurredAt) : IDomainEvent;
