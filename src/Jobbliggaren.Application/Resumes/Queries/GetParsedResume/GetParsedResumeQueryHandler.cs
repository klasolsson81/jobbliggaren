using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResume;

/// <summary>
/// Loads the OWNING job seeker's PendingReview parsed-CV staging artifact and maps it to the
/// detail DTO. Mirrors <c>ReviewParsedResumeQueryHandler</c> EXACTLY (the same fail-closed
/// IDOR shape): resolve the owner from <see cref="ICurrentUser"/>, FirstOrDefault on
/// <c>db.ParsedResumes</c> filtered by Id + JobSeekerId, return null on not-found OR cross-user
/// (logging the cross-user attempt), else map. The aggregate is materialised inside the warmed
/// field-encryption pipeline (the query is <c>IRequiresFieldEncryptionKey</c>), so the
/// decryption interceptor decrypts the CV-PII shadows on read — this handler is the only thing
/// that touches the DbContext + DEK pipeline, and never logs the content (Invariant 3 / §5).
/// </summary>
public sealed class GetParsedResumeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetParsedResumeQuery, ParsedResumeDetailDto?>
{
    public async ValueTask<ParsedResumeDetailDto?> Handle(
        GetParsedResumeQuery query, CancellationToken cancellationToken)
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
            // Identical NotFound for cross-user and unknown — no enumeration oracle. A
            // promoted/discarded artifact is excluded by the global DeletedAt filter and
            // reads as a plain not-found (no cross-user log), parity ReviewParsedResume.
            var exists = await db.ParsedResumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == parsedResumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value, "GetParsedResume");
            }
            return null;
        }

        return resume.ToDetailDto();
    }
}
