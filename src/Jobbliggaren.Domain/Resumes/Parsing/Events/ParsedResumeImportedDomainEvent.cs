using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Domain.Resumes.Parsing.Events;

/// <summary>
/// Raised when a CV is imported and parsed into a <see cref="ParsedResume"/> (F4-8).
/// Carries only non-PII metadata (overall confidence + whether a personnummer was
/// flagged) — never the CV content (ADR 0074 Invariant 3, CLAUDE.md §5).
/// </summary>
public sealed record ParsedResumeImportedDomainEvent(
    ParsedResumeId ParsedResumeId,
    JobSeekerId JobSeekerId,
    OverallConfidenceLevel OverallConfidence,
    bool PersonnummerFlagged,
    DateTimeOffset OccurredAt) : IDomainEvent;
