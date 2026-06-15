using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Improvement.Queries.SuggestCvImprovements;

/// <summary>
/// Loads the OWNING job seeker's parsed CV (decrypted inside the warmed field-encryption
/// pipeline — Invariant 3), runs the F4-9 review, then the deterministic improvement engine
/// (CTO Q2: the handler composes review → improvement; the engine never injects the review
/// engine). Mirrors <c>ReviewParsedResumeQueryHandler</c>: resolve owner from
/// <see cref="ICurrentUser"/>, FirstOrDefault on <c>db.ParsedResumes</c> filtered by Id +
/// JobSeekerId, return null on not-found OR cross-user (logging the cross-user attempt), else
/// propose and map to the transport DTO. The engines take the decrypted aggregate — this
/// handler is the only thing that touches the DbContext + the DEK pipeline.
/// </summary>
public sealed class SuggestCvImprovementsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvReviewEngine reviewEngine,
    ICvImprovementEngine improvementEngine,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<SuggestCvImprovementsQuery, CvImprovementDto?>
{
    public async ValueTask<CvImprovementDto?> Handle(
        SuggestCvImprovementsQuery query, CancellationToken cancellationToken)
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
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value, "SuggestCvImprovements");
            }
            return null;
        }

        // The validator guarantees a parseable RenderProfile (fail-loud, case-sensitive).
        var profile = Enum.Parse<RenderProfile>(query.Profile);
        var review = await reviewEngine.ReviewAsync(resume, profile, cancellationToken);
        var result = await improvementEngine.SuggestAsync(resume, review, profile, cancellationToken);
        return result.ToDto();
    }
}
