using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// Reconciles a Resume's finding-status ledger with the engine's current review after a
/// content write (Fas 4b PR-8, ADR 0093 §D5(b); CTO-bind PR-8 Q1). Called by EVERY
/// master-content/language write handler inside the same UnitOfWork as the write itself
/// (pinned by the reconciliation arch tripwire) so the DEK-free ledger — the hub badge's
/// only data source — can never silently drift from the content it describes. Runs the
/// engine under BOTH render profiles (verdicts are profile-independent; the profile only
/// filters the criterion set) and unions the verdicts, so AtsOnly and VisualOnly
/// criteria both reach the ledger.
/// </summary>
public interface IResumeReviewReconciler
{
    /// <summary>
    /// Recomputes the review over the aggregate's master content and syncs the ledger
    /// via <c>Resume.ReconcileFindingStatuses</c>. When
    /// <paramref name="autoResolveCriteria"/> is non-null (the frame-apply path, PR-7
    /// CTO D-D), a criterion in the set whose post-write verdict genuinely cleared to
    /// Pass is flipped to Resolved first — the engine, not the click, decides; a
    /// partial fix stays Open. Pass null on every other write path.
    /// </summary>
    ValueTask<Result> ReconcileAsync(
        Resume resumeAggregate,
        IReadOnlyCollection<string>? autoResolveCriteria,
        CancellationToken cancellationToken);
}
