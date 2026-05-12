using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Waitlist.Events;

public sealed record WaitlistEntryRequestedDomainEvent(
    WaitlistEntryId WaitlistEntryId,
    string Email,
    DateTimeOffset OccurredAt) : IDomainEvent;
