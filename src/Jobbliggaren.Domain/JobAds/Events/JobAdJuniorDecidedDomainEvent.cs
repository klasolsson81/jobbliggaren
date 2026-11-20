using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds.Events;

/// <summary>#2264 — an admin decided whether an ad is a junior job, or handed it back to the rules (null).</summary>
public sealed record JobAdJuniorDecidedDomainEvent(
    JobAdId JobAdId,
    JuniorDecision? Decision,
    DateTimeOffset OccurredAt) : IDomainEvent;
