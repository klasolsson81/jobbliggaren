using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.DownloadResumeFile;

/// <summary>
/// Loads the OWNING job seeker's stored original for one CANONICAL Resume and decrypts it. Same
/// fail-closed IDOR shape as its staging sibling: resolve the owner from
/// <see cref="ICurrentUser"/>, resolve the resume owner-scoped, follow
/// <c>Resume.SourceParsedResumeId</c> to the file, return null on not-found OR cross-user (logging
/// ONLY the cross-user attempt — no enumeration oracle), else open the Form C envelope via
/// <see cref="IBinaryFieldOpener"/>.
///
/// <para>The enumeration probe runs against the id the CALLER supplied — the ResumeId — because
/// that is the only id an attacker can vary here.</para>
///
/// <para>The link is followed off the RESUME row, never by reading <c>ParsedResumes</c>: the
/// promoted arm of the retention sweep hard-deletes the staging row and deliberately keeps the
/// original ("graduation", DPIA M-F3), so joining the parse would 404 exactly the promoted CVs
/// this endpoint serves. Soft-deleted resumes are excluded by the aggregate's global query filter,
/// so a deleted CV stops serving its original without a second predicate here.</para>
///
/// <para>A null <c>SourceParsedResumeId</c> (a template-built resume) and a missing file row (a
/// declined personnummer consent, or an import predating PR-9a) are BOTH ordinary absence, not
/// access failures: they return null and log nothing.</para>
/// </summary>
public sealed class DownloadResumeOriginalQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IBinaryFieldOpener opener)
    : IQueryHandler<DownloadResumeOriginalQuery, ResumeFileDownloadDto?>
{
    public async ValueTask<ResumeFileDownloadDto?> Handle(
        DownloadResumeOriginalQuery query, CancellationToken cancellationToken)
    {
        var jobSeekerId = await ResumeOriginalReader.ResolveOwnerAsync(db, currentUser, cancellationToken);
        if (jobSeekerId == default)
            return null;

        var resumeId = new ResumeId(query.ResumeId);
        var sourceParsedResumeId = await db.Resumes
            .AsNoTracking()
            .Where(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId)
            .Select(r => r.SourceParsedResumeId)
            .FirstOrDefaultAsync(cancellationToken);

        if (sourceParsedResumeId is null)
        {
            // Two different absences collapse into one null above: the resume is not the caller's
            // (or does not exist), and the resume exists but was built from a template. Only the
            // first can be an access attempt, so the probe asks whether the ROW exists for someone
            // else — a template-built resume the caller owns never reaches it.
            var belongsToAnotherUser = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId && r.JobSeekerId != jobSeekerId, cancellationToken);
            if (belongsToAnotherUser)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", query.ResumeId, currentUser.UserId!.Value, "DownloadResumeOriginal");
            }

            return null;
        }

        // Owner-scoped a second time: the file table is the authority on its own ownership, and
        // re-filtering costs one indexed predicate rather than trusting the hop through the resume.
        var file = await db.ResumeFiles
            .AsNoTracking()
            .Where(f => f.ParsedResumeId == sourceParsedResumeId.Value && f.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        // Ordinary absence (declined consent, pre-PR-9a import) — the caller owns the resume, so
        // there is no access attempt to log.
        return file is null ? null : ResumeOriginalReader.Open(opener, file);
    }
}
