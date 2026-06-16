using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Domain.Resumes.Events;

/// <summary>
/// Raised when a canonical <c>Resume</c> is created by promoting a
/// <see cref="ParsedResume"/> staging artifact (Fas 4 STEG A, CTO DQ5b). Captures the
/// provenance link (which parsed artifact became which resume) for the audit/GDPR trail —
/// the link lives only as this in-memory domain event, NOT as a persisted column
/// (CTO DQ5b — no migration). Carries only ids; never any CV content (ADR 0074
/// Invariant 3, CLAUDE.md §5).
/// </summary>
public sealed record ResumeCreatedFromParsedResumeDomainEvent(
    ResumeId ResumeId,
    ParsedResumeId SourceParsedResumeId,
    JobSeekerId JobSeekerId,
    DateTimeOffset OccurredAt) : IDomainEvent;
