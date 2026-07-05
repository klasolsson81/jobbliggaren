using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes.Events;

/// <summary>
/// Raised when a CV's template/display options change
/// (<see cref="Resume.ChangeTemplateOptions"/>, Fas 4b PR-3 — ADR 0096). Ids +
/// timestamp only (ADR 0074 Invariant 3); the chosen values are readable from the
/// aggregate, not the event. Raise-only/in-memory (house doctrine — no dispatcher).
/// </summary>
public sealed record ResumeTemplateOptionsChangedDomainEvent(
    ResumeId ResumeId,
    DateTimeOffset OccurredAt) : IDomainEvent;
