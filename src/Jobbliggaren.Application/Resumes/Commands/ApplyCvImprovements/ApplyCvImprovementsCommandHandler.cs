using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.FrameApply;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.ApplyCvImprovements;

/// <summary>
/// The frame-apply write path (Fas 4b PR-7, #656; ADR 0093 §D2 — handoff §6.2 "Åtgärda
/// direkt"). Owner-scoped with IDOR 404-parity (parity <c>SetFindingStatusCommandHandler</c>).
/// Flow: recompute the canonical review (compute-on-demand, D8) → per change: resolve the
/// finding by the client's ECHOED server fingerprint (mismatch → 409 "CV changed,
/// re-review"), ground the slots, build the After via <c>FromFrame</c> (the only builder —
/// no client text, ADR 0074 Inv. 2), patch the running content copy sequentially → run the
/// SHARED personnummer guard on the composed content BEFORE the sink (ADR 0074 Inv. 1 —
/// the #650 arch tripwire discovers this handler through the <c>UpdateMasterContent</c>
/// sink and fails the build if the guard call disappears) → write ONCE through
/// <c>Resume.UpdateMasterContent</c> (ADR 0021; StaleAt stamping on other resolved
/// findings rides along) → reconcile the ledger via the shared
/// <c>IResumeReviewReconciler</c> with the applied criterion ids (Fas 4b PR-8, CTO-bind
/// Q1 — the PR-7 auto-resolve loop folded in: only an applied criterion whose verdict
/// genuinely cleared flips to Resolved, CTO D-D — a partial fix stays Open; the engine,
/// not the click, decides).
/// <para>The review runs at the fixed <see cref="RenderProfile.Ats"/>: every frame
/// criterion (A1/A2/C3) is a Both-profile prose criterion, and the
/// <c>FrameCriterionMismatch</c> gate keeps non-frame criteria out of this path — a
/// future VisualOnly frame criterion is a frames.v1.json change and revisits this.</para>
/// </summary>
public sealed class ApplyCvImprovementsCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvReviewEngine reviewEngine,
    IFrameProvider frameProvider,
    IVerbMapper verbMapper,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger,
    IResumeReviewReconciler reconciler,
    IFindingFingerprinter fingerprinter)
    : ICommandHandler<ApplyCvImprovementsCommand, Result>
{
    public async ValueTask<Result> Handle(
        ApplyCvImprovementsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var resumeId = new ResumeId(command.ResumeId);
        var resume = await db.Resumes
            .Include(r => r.Versions)
            .Include(r => r.FindingStatuses)
            .FirstOrDefaultAsync(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId, cancellationToken);

        if (resume is null)
        {
            // IDOR 404-parity: unknown vs cross-user are indistinguishable to the caller;
            // only the genuine cross-user attempt is logged.
            var exists = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "ApplyCvImprovements");
            }

            throw new NotFoundException("CV hittades inte.");
        }

        var catalog = frameProvider.GetFrameCatalog();
        var strongVerbs = FrameApplyComposer.BuildStrongVerbSet(verbMapper.GetVerbMapping(), catalog);

        // Pre-apply review over the CURRENT canonical content — the substrate every
        // change's fingerprint and Before line resolve against (server recompute, §D2).
        var content = resume.MasterVersion.Content;
        var review = await reviewEngine.ReviewAsync(
            CvReviewContext.FromCanonical(
                content, ResumeContentLinearizer.Linearize(content), resume.Language),
            RenderProfile.Ats,
            cancellationToken);

        // Sequential compose over a running immutable copy: a later change whose line an
        // earlier change consumed conflicts honestly instead of double-rewriting.
        var patched = content;
        var appliedCriteria = new List<string>();
        foreach (var change in command.Changes)
        {
            var frame = catalog.Frames.FirstOrDefault(f =>
                string.Equals(f.Id, change.FrameId, StringComparison.Ordinal));
            if (frame is null)
            {
                return Result.Failure(DomainError.Validation(
                    "Resume.FrameUnknown", "Ramen finns inte i den aktuella ram-katalogen."));
            }

            if (!frame.CriterionIds.Contains(change.CriterionId, StringComparer.Ordinal))
            {
                return Result.Failure(DomainError.Validation(
                    "Resume.FrameCriterionMismatch", "Ramen åtgärdar inte det angivna kriteriet."));
            }

            var resolved = FrameApplyComposer.ResolveFinding(
                review, change.CriterionId, change.FindingFingerprint, patched, fingerprinter);
            if (resolved.IsFailure)
                return Result.Failure(resolved.Error);

            var grounding = FrameSlotGrounding.Validate(
                frame, change.SlotInputs, resolved.Value.Line, strongVerbs);
            if (grounding.IsFailure)
                return grounding;

            // Grounding passed and the validator bans braces in slot values, so the factory
            // does not throw on client-shaped input — it re-runs the same rules plus a
            // residual-placeholder guard (defense-in-depth) and is the ONLY builder of the
            // After (no client text). A throw here is a server bug, not a client error.
            var proposed = ProposedChange.FromFrame(
                targetId: $"frame:{change.CriterionId}",
                category: resolved.Value.Verdict.Category,
                criterionId: change.CriterionId,
                evidence: new TextSpanEvidence(
                    new TextSpan(TextSpan.NotLocated, resolved.Value.Line.Length, resolved.Value.Line),
                    Note: null),
                frame: frame,
                slotInputs: change.SlotInputs,
                strongVerbSet: strongVerbs,
                rationale: "Omskriven via deterministisk ram (Åtgärda direkt).");

            var applied = FrameApplyComposer.ApplyToContent(
                patched, resolved.Value.Line, proposed.Replacement!.After);
            if (applied.IsFailure)
                return Result.Failure(applied.Error);

            patched = applied.Value;
            appliedCriteria.Add(change.CriterionId);
        }

        // ADR 0074 Invariant 1: the shared guard runs on the FULL composed content before
        // it becomes canonical — a personnummer smuggled via a free-echo Text slot (or
        // composed into surrounding context) blocks the whole apply, nothing is written.
        var guard = ResumeContentPersonnummerGuard.Check(ResumeContentMapper.ToDto(patched));
        if (guard.IsFailure)
            return guard;

        // The ONE canonical write path (ADR 0021/D2(b)) — StaleAt stamping on previously
        // resolved findings rides along for free.
        var updated = resume.UpdateMasterContent(patched, clock);
        if (updated.IsFailure)
            return updated;

        // Post-write reconcile (Fas 4b PR-8, CTO-bind Q1 — the PR-7 verdict-verified
        // auto-resolve loop folded into the shared reconciler, DRY): recompute on the NEW
        // content; only an applied criterion whose verdict genuinely cleared to an
        // ASSESSED Pass flips to Resolved (CTO D-D — the engine, not the click, decides;
        // a partial fix stays Open), and the ledger is seeded/pruned for the hub badge.
        // The reconciler completes or THROWS (CTO bind 2026-07-17): a throw propagates
        // past this handler, the unconditional UnitOfWork save never runs, and the
        // tracked apply rolls back with it.
        await reconciler.ReconcileAsync(
            resume, appliedCriteria.Distinct(StringComparer.Ordinal).ToList(), cancellationToken);
        return Result.Success();
    }
}
