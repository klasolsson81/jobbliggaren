using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;

/// <summary>
/// Loads the OWNING job seeker's parsed CV (decrypted inside the warmed field-encryption
/// pipeline — Invariant 3) and runs the deterministic review engine. Mirrors
/// <c>GetResumeByIdQueryHandler</c>: resolve owner from <see cref="ICurrentUser"/>,
/// FirstOrDefault on <c>db.ParsedResumes</c> filtered by Id + JobSeekerId, return null on
/// not-found OR cross-user (logging the cross-user attempt), else review and map to the
/// transport DTO. The engine takes the decrypted aggregate — this handler is the only
/// thing that touches the DbContext + the DEK pipeline.
/// </summary>
public sealed class ReviewParsedResumeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvReviewEngine engine,
    IRubricProvider rubricProvider,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<ReviewParsedResumeQuery, CvReviewDto?>
{
    public async ValueTask<CvReviewDto?> Handle(
        ReviewParsedResumeQuery query, CancellationToken cancellationToken)
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

        var parsedResumeId = new ParsedResumeId(query.ParsedResumeId);
        var resume = await db.ParsedResumes
            .AsNoTracking()
            .Where(r => r.Id == parsedResumeId && r.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (resume is null)
        {
            var exists = await db.ParsedResumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == parsedResumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value, "ReviewParsedResume");
            }
            return null;
        }

        // The validator guarantees a parseable RenderProfile (fail-loud, case-sensitive).
        var profile = Enum.Parse<RenderProfile>(query.Profile);
        // Staging adapter (Fas 4b PR-4, ADR 0093 §D8): the parsed CV reviews against its
        // own RawText substrate — same engine as the canonical review, different adapter.
        var result = await engine.ReviewAsync(
            CvReviewContext.FromParsed(resume), profile, cancellationToken);

        // Supply the criterionId→Name lookup from the rubric (the single source of truth for
        // the human heading) so the DTO leads with a readable title, not the cryptic id.
        var nameByCriterionId = rubricProvider.GetRubric().Criteria
            .ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        return result.ToDto(nameByCriterionId);
    }
}
