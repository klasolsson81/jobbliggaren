using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewResume;

/// <summary>
/// Loads the OWNING job seeker's canonical Resume (Master content decrypted inside the
/// warmed field-encryption pipeline — Invariant 3), linearizes it via the shared
/// linearizer (ADR 0093 §D8 SPOT) and runs the deterministic review engine through the
/// canonical adapter. Mirrors <c>ReviewParsedResumeQueryHandler</c>: owner-resolve,
/// FirstOrDefault by Id + JobSeekerId, cross-user attempt logged, null on not-found.
/// The response merges the persisted finding-status overlay (D2(e)) onto the freshly
/// computed verdicts — see <see cref="FindingStatusOverlay"/> for the honesty rules.
/// </summary>
public sealed class ReviewResumeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvReviewEngine engine,
    IRubricProvider rubricProvider,
    IFailedAccessLogger failedAccessLogger,
    IFindingFingerprinter fingerprinter)
    : IQueryHandler<ReviewResumeQuery, CvReviewDto?>
{
    public async ValueTask<CvReviewDto?> Handle(
        ReviewResumeQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return null;

        var resumeId = new ResumeId(query.ResumeId);
        var resume = await db.Resumes
            .AsNoTracking()
            .Include(r => r.Versions)
            .Include(r => r.FindingStatuses)
            .Where(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (resume is null)
        {
            var exists = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "ReviewResume");
            }
            return null;
        }

        // The validator guarantees a parseable RenderProfile (fail-loud, case-sensitive).
        var profile = Enum.Parse<RenderProfile>(query.Profile);

        var content = resume.MasterVersion.Content;
        var linearized = ResumeContentLinearizer.Linearize(content);
        var context = CvReviewContext.FromCanonical(content, linearized, resume.Language);
        var result = await engine.ReviewAsync(context, profile, cancellationToken);

        var overlay = FindingStatusOverlay.Build(resume.FindingStatuses, result, fingerprinter);

        // One GetRubric() materialization feeds every per-criterion projection (heading,
        // IsIgnorable, the "Godkänns när" clause). The canonical review is the one the status UI
        // acts on, so its IsIgnorable drives the Ignorera gate.
        return result.ToDto(rubricProvider.GetRubric().Criteria, overlay);
    }
}
