using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes.Events;

/// <summary>
/// Raised when the user records a decision on one review finding
/// (<see cref="Resume.SetFindingStatus"/>, Fas 4b PR-4, ADR 0093 §D2(e)). Ids, closed
/// tokens and a timestamp only — never CV content (ADR 0074 Invariant 3). Raise-only/
/// in-memory; the audit row for the user action comes from the command's
/// <c>IAuditableCommand</c> marker, not from this event. Downstream badge-cache
/// invalidation (PR-8, D5b) may subscribe here.
/// </summary>
public sealed record ResumeFindingStatusChangedDomainEvent(
    ResumeId ResumeId,
    string RubricVersion,
    string CriterionId,
    ReviewFindingStatus Status,
    DateTimeOffset OccurredAt) : IDomainEvent;
