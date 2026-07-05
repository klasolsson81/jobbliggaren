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
/// computed verdicts — see <see cref="BuildStatusOverlay"/> for the honesty rules.
/// </summary>
public sealed class ReviewResumeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvReviewEngine engine,
    IRubricProvider rubricProvider,
    IFailedAccessLogger failedAccessLogger)
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

        var overlay = BuildStatusOverlay(resume.FindingStatuses, result);

        var nameByCriterionId = rubricProvider.GetRubric().Criteria
            .ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        return result.ToDto(nameByCriterionId, overlay);
    }

    /// <summary>
    /// Merges the persisted status ledger onto the fresh verdicts (CTO-bind PR-4 Q3
    /// interaction). Coarse staleness (<c>StaleAt</c>, stamped on content change) is
    /// resolved by the FINE fingerprint: a Resolved-and-stale decision whose fingerprint
    /// still matches the criterion's current finding is genuinely unresolved → surfaced
    /// with its staleness ("marked fixed, still present"); one whose fingerprint no
    /// longer matches is about a finding that no longer exists → silently cleared (never
    /// a fabricated lingering warning, CLAUDE.md §5). Fresh decisions pass through as-is;
    /// statuses keyed to another rubric version never carry over (D2(e) key boundary).
    /// </summary>
    private static Dictionary<string, (string Status, DateTimeOffset? StaleAt)> BuildStatusOverlay(
        IReadOnlyList<ResumeFindingStatus> statuses, CvReviewResult result)
    {
        var version = result.RubricVersion.ToString();
        var byCriterion = statuses
            .Where(s => s.RubricVersion == version)
            .ToDictionary(s => s.CriterionId, StringComparer.Ordinal);

        var overlay = new Dictionary<string, (string, DateTimeOffset?)>(StringComparer.Ordinal);
        foreach (var verdict in result.Verdicts)
        {
            if (!byCriterion.TryGetValue(verdict.CriterionId, out var row))
                continue;

            if (row.Status == ReviewFindingStatus.Resolved && row.StaleAt is not null)
            {
                var currentFingerprint = FindingTargetFingerprint.Compute(result.RubricVersion, verdict);
                if (!string.Equals(currentFingerprint, row.TargetFingerprint, StringComparison.Ordinal))
                    continue;
            }

            overlay[verdict.CriterionId] = (row.Status.Name, row.StaleAt);
        }

        return overlay;
    }
}
