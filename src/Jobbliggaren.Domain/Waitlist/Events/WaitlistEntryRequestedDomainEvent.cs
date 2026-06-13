using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Waitlist.Events;

public sealed record WaitlistEntryRequestedDomainEvent(
    WaitlistEntryId WaitlistEntryId,
    string Email,
    DateTimeOffset OccurredAt) : IDomainEvent;
