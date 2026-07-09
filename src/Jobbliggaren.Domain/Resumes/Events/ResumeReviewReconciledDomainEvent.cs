using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes.Events;

/// <summary>
/// Raised when a content write reconciled the finding-status ledger against the
/// engine's current review (<see cref="Resume.ReconcileFindingStatuses"/>, Fas 4b PR-8,
/// ADR 0093 §D5(b)). One aggregate-level event per reconcile — not one per row — since
/// the reconcile is a single logical outcome of the write. Ids, a version token and a
/// count only, never CV content (ADR 0074 Invariant 3). Raise-only/in-memory; the badge
/// projection reads the ledger rows themselves, this event exists for observability and
/// future cache invalidation.
/// </summary>
public sealed record ResumeReviewReconciledDomainEvent(
    ResumeId ResumeId,
    string RubricVersion,
    int OpenCount,
    DateTimeOffset OccurredAt) : IDomainEvent;
