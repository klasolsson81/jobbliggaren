using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Waitlist.Events;

public sealed record WaitlistEntryRejectedDomainEvent(
    WaitlistEntryId WaitlistEntryId,
    Guid RejectedByAdminId,
    DateTimeOffset OccurredAt) : IDomainEvent;
