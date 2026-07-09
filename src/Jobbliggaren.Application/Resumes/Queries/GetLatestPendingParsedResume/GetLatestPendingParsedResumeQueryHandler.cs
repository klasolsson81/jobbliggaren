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

        // Gaps is a value-converted jsonb VO: PROJECT IT WHOLE and map in memory — member
        // access into a converted property does not translate on Npgsql (it only appears
        // to work on the InMemory provider). Still a pure metadata projection, no
        // aggregate materialisation, no CV-PII columns touched.
        var pending = await db.ParsedResumes
            .AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId && r.Status == ParsedResumeStatus.PendingReview)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { Id = r.Id.Value, r.SourceFileName, r.CreatedAt, r.Gaps })
            .FirstOrDefaultAsync(cancellationToken);

        if (pending is null)
            return null;

        // Denormalized non-PII presence flags (Fas 4b PR-8, CTO-bind Q5); null for a
        // pre-PR-8 import — the card renders without a meter rather than guessing.
        var gaps = pending.Gaps is null
            ? null
            : new ParsedGapSummaryDto(
                pending.Gaps.HasFullName,
                pending.Gaps.HasEmail,
                pending.Gaps.HasPhone,
                pending.Gaps.HasLocation,
                pending.Gaps.HasProfile,
                pending.Gaps.HasExperience,
                pending.Gaps.HasEducation,
                pending.Gaps.HasSkills,
                pending.Gaps.HasLanguages);

        return new PendingParsedResumeSummaryDto(
            pending.Id, pending.SourceFileName, pending.CreatedAt, gaps);
    }
}
