using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.SetFindingStatus;

/// <summary>
/// Records the user's decision on one review finding (Fas 4b PR-4, ADR 0093 §D2(e)).
/// The fingerprint that identifies the finding instance is SERVER-derived here: the
/// handler recomputes the review for the criterion's profile and fingerprints the
/// CURRENT finding (ADR 0074 Invariant 2 — parity with D2(a)'s "client-submitted
/// before/after is unsound"). A decision can only be recorded about a finding that
/// exists: the criterion's current verdict must be Fail or Warn (the handoff's finding
/// cards) — reverting to Open is allowed regardless, so a stale decision can always be
/// withdrawn.
/// </summary>
public sealed class SetFindingStatusCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvReviewEngine engine,
    IRubricProvider rubricProvider,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<SetFindingStatusCommand, Result>
{
    public async ValueTask<Result> Handle(
        SetFindingStatusCommand command, CancellationToken cancellationToken)
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
            var exists = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "SetFindingStatus");
            }
            throw new NotFoundException("CV hittades inte.");
        }

        // The validator guarantees a parseable status name (fail-loud, case-sensitive).
        var status = ReviewFindingStatus.FromName(command.Status);

        var rubric = rubricProvider.GetRubric();
        var criterion = rubric.Criteria
            .FirstOrDefault(c => string.Equals(c.Id, command.CriterionId, StringComparison.Ordinal));
        if (criterion is null)
            return Result.Failure(DomainError.NotFound("RubricCriterion", command.CriterionId));

        // Recompute the review through the canonical adapter (compute-on-demand, D8 —
        // one engine, one assessment path). The verdict is profile-independent (rules
        // never branch on the render profile; the profile only filters the criterion
        // SET), so evaluating under the criterion's own profile is deterministic.
        var content = resume.MasterVersion.Content;
        var linearized = ResumeContentLinearizer.Linearize(content);
        var context = CvReviewContext.FromCanonical(content, linearized, resume.Language);
        var profile = criterion.Profile == RubricProfile.VisualOnly
            ? RenderProfile.Visual
            : RenderProfile.Ats;
        var result = await engine.ReviewAsync(context, profile, cancellationToken);

        var verdict = result.Verdicts
            .FirstOrDefault(v => string.Equals(v.CriterionId, command.CriterionId, StringComparison.Ordinal));
        if (verdict is null)
            return Result.Failure(DomainError.NotFound("RubricCriterion", command.CriterionId));

        // A decision needs a finding to be about (Fail/Warn — the handoff's finding
        // cards). Pass/NotAssessed criteria carry nothing to resolve or ignore. Open
        // (revert) is always allowed so a recorded decision can be withdrawn even after
        // the underlying finding disappeared.
        if (status != ReviewFindingStatus.Open
            && verdict.Verdict is not (CriterionVerdict.Fail or CriterionVerdict.Warn))
        {
            return Result.Failure(DomainError.Validation(
                "Resume.FindingNotActionable",
                "Kriteriet har ingen anmärkning att markera i den aktuella granskningen."));
        }

        // Ignored is a style opt-out only (handoff §5.3 "Ignorera regeln endast för
        // stilfrågor"; PR-5 CTO-bind D2): a non-style rule is a substance finding the user
        // cannot silence by choice. Server-enforced here — the criterion's StyleOnly flag
        // is versioned rubric DATA (fail-closed default false), never trusted from the
        // client and never a hardcoded C# category list (§5). Resolved ("jag fixar det
        // själv") and Open stay allowed on every criterion.
        if (status == ReviewFindingStatus.Ignored && !criterion.StyleOnly)
        {
            return Result.Failure(DomainError.Validation(
                "Resume.FindingNotIgnorable",
                "Den här regeln kan inte ignoreras. Bara stilregler kan ignoreras."));
        }

        var fingerprint = FindingTargetFingerprint.Compute(result.RubricVersion, verdict);
        return resume.SetFindingStatus(
            result.RubricVersion.ToString(), command.CriterionId, status, fingerprint, clock);
    }
}
