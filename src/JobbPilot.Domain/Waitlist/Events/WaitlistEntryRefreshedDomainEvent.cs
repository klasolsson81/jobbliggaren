using JobbPilot.Domain.Common;

namespace JobbPilot.Domain.Waitlist.Events;

/// <summary>
/// Raised när en pending <see cref="WaitlistEntry"/> uppdateras vid re-signup
/// (samma email + ny motivering/samtycke). Bär ingen PII utöver entity-id —
/// audit-handlers gör read-projection mot aggregate-tabellen för fritext-fält.
/// </summary>
public sealed record WaitlistEntryRefreshedDomainEvent(
    WaitlistEntryId WaitlistEntryId,
    DateTimeOffset OccurredAt) : IDomainEvent;
