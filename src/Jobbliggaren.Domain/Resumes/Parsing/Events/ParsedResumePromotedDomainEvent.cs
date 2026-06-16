using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Resumes.Parsing.Events;

/// <summary>
/// Raised when a <see cref="ParsedResume"/> staging artifact is promoted to a canonical
/// <c>Resume</c> (Fas 4 STEG A). The artifact is soft-deleted and marked
/// <see cref="ParsedResumeStatus.Promoted"/> at the same time (CTO DQ7 — retained for
/// audit until the staging-retention sweep). Carries only non-PII metadata (ids) —
/// never the CV content (ADR 0074 Invariant 3, CLAUDE.md §5).
/// </summary>
public sealed record ParsedResumePromotedDomainEvent(
    ParsedResumeId ParsedResumeId,
    JobSeekerId JobSeekerId,
    DateTimeOffset OccurredAt) : IDomainEvent;
