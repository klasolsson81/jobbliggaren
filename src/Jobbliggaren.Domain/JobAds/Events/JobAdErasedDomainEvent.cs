using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.JobAds.Events;

/// <summary>
/// Raised when a JobAd is erased under GDPR Art. 17 (ADR 0106 Tier B, #842).
/// </summary>
/// <remarks>
/// Carries the <see cref="ExternalId"/> deliberately: it is the accountability spine of the
/// erasure (Art. 5(2)) and it is <b>not</b> the recruiter's personal data — it is
/// Arbetsförmedlingen's identifier for a public advertisement. The recruiter's identifier
/// never appears on this event, and never reaches a log sink (CLAUDE.md §5).
/// </remarks>
public sealed record JobAdErasedDomainEvent(
    JobAdId JobAdId,
    string? ExternalId,
    DateTimeOffset OccurredAt) : IDomainEvent;
