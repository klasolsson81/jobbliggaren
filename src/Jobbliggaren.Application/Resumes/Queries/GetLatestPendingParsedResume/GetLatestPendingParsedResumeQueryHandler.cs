using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.GetLatestPendingParsedResume;

/// <summary>
/// Returns the CURRENT user's most-recent PendingReview parsed-CV summary (id + source file name +
/// upload time), or null when the user has no pending CV. Owner-scoped by construction: the
/// jobSeekerId is resolved from the authenticated user and the query is filtered to it — there is no
/// client-supplied id, so no IDOR/enumeration surface (unlike <c>GetParsedResumeOccupations</c>).
///
/// PROJECTS the plaintext metadata columns and never materialises the aggregate: materialising would
/// hit the <c>FieldDecryptionMaterializationInterceptor</c> on the CV-PII shadows with no warmed DEK
/// (this query is intentionally NOT <c>IRequiresFieldEncryptionKey</c>) → throw, and would decrypt
/// PII this read never uses (PII-minimisation, CLAUDE.md §5). A promoted/discarded artifact is
/// soft-deleted and excluded by the global DeletedAt filter; <c>Status == PendingReview</c> is kept
/// explicit so the intent survives any future change to that filter.
/// </summary>
public sealed class GetLatestPendingParsedResumeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<GetLatestPendingParsedResumeQuery, PendingParsedResumeSummaryDto?>
{
    public async ValueTask<PendingParsedResumeSummaryDto?> Handle(
        GetLatestPendingParsedResumeQuery query, CancellationToken cancellationToken)
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

        return await db.ParsedResumes
            .AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId && r.Status == ParsedResumeStatus.PendingReview)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new PendingParsedResumeSummaryDto(
                r.Id.Value,
                r.SourceFileName,
                r.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
