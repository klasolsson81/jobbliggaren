using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Review;

/// <summary>
/// The one engine-driven ledger write path (Fas 4b PR-8, CTO-bind Q1 — ADR 0097
/// amendment 2026-07-09). Composes existing parts, adds no policy of its own: canonical
/// context (D8) → engine under both profiles → union by criterion → server-derived
/// fingerprints → <c>Resume.ReconcileFindingStatuses</c>. The PR-7 verdict-verified
/// auto-resolve loop lives here now (folded per CTO Q1, DRY) —
/// <c>ApplyCvImprovementsCommandHandler</c> passes its applied criterion ids, every
/// other caller passes null.
/// </summary>
public sealed class ResumeReviewReconciler(
    ICvReviewEngine engine,
    IFindingFingerprinter fingerprinter,
    IDateTimeProvider clock) : IResumeReviewReconciler
{
    public async ValueTask ReconcileAsync(
        Resume resumeAggregate,
        IReadOnlyCollection<string>? autoResolveCriteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resumeAggregate);

        var content = resumeAggregate.MasterVersion.Content;
        var linearized = ResumeContentLinearizer.Linearize(content);
        var context = CvReviewContext.FromCanonical(content, linearized, resumeAggregate.Language);

        // Verdicts are profile-independent (rules never branch on the render profile;
        // the profile only filters the criterion SET — see SetFindingStatusCommandHandler),
        // so running both profiles and unioning first-wins yields one verdict per
        // criterion covering Both + AtsOnly + VisualOnly.
        var ats = await engine.ReviewAsync(context, RenderProfile.Ats, cancellationToken);
        var visual = await engine.ReviewAsync(context, RenderProfile.Visual, cancellationToken);

        var union = new Dictionary<string, CvCriterionVerdict>(StringComparer.Ordinal);
        foreach (var verdict in ats.Verdicts)
            union.TryAdd(verdict.CriterionId, verdict);
        foreach (var verdict in visual.Verdicts)
            union.TryAdd(verdict.CriterionId, verdict);

        var rubricVersion = ats.RubricVersion;

        // Folded PR-7 auto-resolve (CTO D-D): only an APPLIED criterion whose verdict
        // genuinely cleared to an ASSESSED Pass flips to Resolved (fresh server-derived
        // fingerprint; ChangeStatus clears any just-stamped StaleAt). A still-flagged
        // Fail/Warn is a partial fix and stays actionable below; NotAssessed is "could
        // not assess", never evidence the finding is gone.
        if (autoResolveCriteria is not null)
        {
            foreach (var criterionId in autoResolveCriteria.Distinct(StringComparer.Ordinal))
            {
                if (!union.TryGetValue(criterionId, out var verdict)
                    || verdict.Verdict != CriterionVerdict.Pass)
                {
                    continue;
                }

                var fingerprint = fingerprinter.Compute(rubricVersion, verdict);
                var set = resumeAggregate.SetFindingStatus(
                    rubricVersion.ToString(), criterionId,
                    ReviewFindingStatus.Resolved, fingerprint, clock);
                if (set.IsFailure)
                {
                    // Every input is server-derived (version/criterion/fingerprint shapes
                    // by construction), so a failure is a server bug — fail loud rather
                    // than persist a half-reconciled ledger (PR-7 code review Minor 1
                    // precedent: the content write persists via the unconditional UoW).
                    throw new InvalidOperationException(
                        $"Auto-resolve failed for server-derived finding-status inputs " +
                        $"({set.Error.Code}) — inconsistent post-apply state.");
                }
            }
        }

        var actionable = union.Values
            .Where(v => v.Verdict is CriterionVerdict.Fail or CriterionVerdict.Warn)
            .Select(v => new ReviewFindingSnapshot(
                v.CriterionId, fingerprinter.Compute(rubricVersion, v)))
            .ToList();

        var reconciled = resumeAggregate.ReconcileFindingStatuses(
            rubricVersion.ToString(), actionable, clock);
        if (reconciled.IsFailure)
        {
            // Same posture as the auto-resolve branch above (dotnet-architect PR-8.1
            // Minor; §3 two-idiom rule): every input is server-derived, so a failure is
            // a server bug — surfacing it as a Result would render a 4xx "client error"
            // for a broken invariant (§5 never mis-report).
            throw new InvalidOperationException(
                $"Ledger reconcile failed for server-derived inputs " +
                $"({reconciled.Error.Code}) — inconsistent review state.");
        }
    }
}
