using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Waitlist.Events;

public sealed record WaitlistEntryRejectedDomainEvent(
    WaitlistEntryId WaitlistEntryId,
    Guid RejectedByAdminId,
    DateTimeOffset OccurredAt) : IDomainEvent;
