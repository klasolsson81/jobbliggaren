using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// One row of the DEK-free finding-status ledger (Fas 4b PR-4, ADR 0093 §D2(e)) — the
/// user's decision on one rubric criterion's finding for one CV, keyed
/// (<c>ResumeId</c>, <see cref="RubricVersion"/>, <see cref="CriterionId"/>). A child
/// entity INSIDE the <see cref="Resume"/> aggregate (ResumeVersion precedent, CTO-bind
/// PR-4 Q1): it has no lifecycle of its own, is written only through Resume root methods
/// (<see cref="Resume.SetFindingStatus"/>), and is deleted by the parent's DB-FK cascade
/// (GDPR Art. 17 — out of the CascadeMap by the fitness test's own design).
/// </summary>
/// <remarks>
/// <b>DEK-free by shape (the D2(e) at-rest guarantee):</b> every member is a closed
/// SmartEnum, a bounded machine token (rubric version, criterion id), a fixed-length hex
/// digest, or a timestamp — NO CV text at rest. Review RESULTS stay compute-on-demand
/// (ADR 0074); this ledger stores only the user's decision about them.
/// <c>ResumeFindingStatusColumnGuardTests</c> pins the shape fail-closed.
/// </remarks>
public sealed class ResumeFindingStatus : Entity<ResumeFindingStatusId>
{
    /// <summary>The rubric version the decision was made against ("major.minor.patch").
    /// A new rubric version is a new key → statuses never carry across versions.</summary>
    public string RubricVersion { get; private set; } = null!;

    /// <summary>The rubric criterion id ("A1".."E8") — a bounded machine token, never free text.</summary>
    public string CriterionId { get; private set; } = null!;

    public ReviewFindingStatus Status { get; private set; } = null!;

    /// <summary>
    /// SHA-256 hex digest identifying the finding INSTANCE the decision was made about
    /// (CTO-bind PR-4 Q4: hash over rubric version + criterion id + normalized evidence;
    /// server-derived, never client-submitted). Content-addressed identity WITHOUT storing
    /// content — lets the next review distinguish "same finding still present" from
    /// "finding gone" (Q3 interaction).
    /// </summary>
    public string TargetFingerprint { get; private set; } = null!;

    /// <summary>
    /// Stamped when the CV's master content changes after a <see cref="ReviewFindingStatus.Resolved"/>
    /// decision (the coarse "content moved under this decision" flag, CTO-bind PR-4 Q3).
    /// Orthogonal to <see cref="Status"/> — the user's decision is preserved, the GET
    /// review response surfaces the re-review need. Cleared on the next explicit decision.
    /// </summary>
    public DateTimeOffset? StaleAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // EF Core constructor
    private ResumeFindingStatus() { }

    private ResumeFindingStatus(
        ResumeFindingStatusId id,
        string rubricVersion,
        string criterionId,
        ReviewFindingStatus status,
        string targetFingerprint,
        DateTimeOffset now) : base(id)
    {
        RubricVersion = rubricVersion;
        CriterionId = criterionId;
        Status = status;
        TargetFingerprint = targetFingerprint;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Inputs are validated by the single mutation path, <see cref="Resume.SetFindingStatus"/>.</summary>
    internal static ResumeFindingStatus Create(
        string rubricVersion,
        string criterionId,
        ReviewFindingStatus status,
        string targetFingerprint,
        DateTimeOffset now) =>
        new(ResumeFindingStatusId.New(), rubricVersion, criterionId, status, targetFingerprint, now);

    /// <summary>A fresh explicit decision replaces the old one and clears any staleness.</summary>
    internal void ChangeStatus(ReviewFindingStatus status, string targetFingerprint, DateTimeOffset now)
    {
        Status = status;
        TargetFingerprint = targetFingerprint;
        StaleAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Content changed under this decision. Only a <see cref="ReviewFindingStatus.Resolved"/>
    /// decision goes stale (CTO-bind PR-4 Q3): <see cref="ReviewFindingStatus.Ignored"/> is a
    /// content-independent rule opt-out and <see cref="ReviewFindingStatus.Open"/> has nothing
    /// to invalidate. Idempotent — an already-stale row keeps its first stamp.
    /// </summary>
    internal void MarkStaleIfResolved(DateTimeOffset now)
    {
        if (Status == ReviewFindingStatus.Resolved && StaleAt is null)
        {
            StaleAt = now;
        }
    }
}
