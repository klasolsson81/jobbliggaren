using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.DownloadResumeFile;

/// <summary>
/// Loads the OWNING job seeker's stored original for one STAGING parse and decrypts it. Mirrors
/// <c>GetParsedResumeQueryHandler</c> EXACTLY for the fail-closed IDOR shape: resolve the owner
/// from <see cref="ICurrentUser"/>, FirstOrDefault on <c>db.ResumeFiles</c> filtered by
/// ParsedResumeId + JobSeekerId, return null on not-found OR cross-user (logging ONLY the
/// cross-user attempt, never an unknown-id typo — no enumeration oracle), else open the Form C
/// envelope via <see cref="IBinaryFieldOpener"/> and return the plaintext bytes.
///
/// <para>The enumeration probe runs against the id the CALLER supplied — the ParsedResumeId —
/// because that is the only id an attacker can vary. Probing some other key would aim the oracle
/// guard at an id no request carries.</para>
///
/// <para>Owner-scoped DIRECTLY against <c>resume_files.job_seeker_id</c>, never by joining
/// <c>ParsedResumes</c>: the staging row is hard-deleted by the retention sweep while the original
/// survives, so a join would turn a live file into a 404.</para>
///
/// <para>The decrypt happens HERE (Application, via the port) — never in the Api composition root
/// and never on the aggregate: the handler reads the opaque <c>SealedContent</c> ciphertext off the
/// aggregate and opens it outside the model (aggregate-honesty, ADR 0100 CTO Q2). The shared
/// open/redact half lives in <see cref="ResumeOriginalReader"/>.</para>
/// </summary>
public sealed class DownloadParsedResumeOriginalQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IBinaryFieldOpener opener)
    : IQueryHandler<DownloadParsedResumeOriginalQuery, ResumeFileDownloadDto?>
{
    public async ValueTask<ResumeFileDownloadDto?> Handle(
        DownloadParsedResumeOriginalQuery query, CancellationToken cancellationToken)
    {
        if (await ResumeOriginalReader.ResolveOwnerAsync(db, currentUser, cancellationToken)
            is not { } owner)
            return null;

        var jobSeekerId = owner.JobSeekerId;
        var parsedResumeId = new ParsedResumeId(query.ParsedResumeId);
        var file = await db.ResumeFiles
            .AsNoTracking()
            .Where(f => f.ParsedResumeId == parsedResumeId && f.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (file is null)
        {
            // Identical NotFound for cross-user and unknown — no enumeration oracle. Log the
            // cross-user attempt ONLY when an original exists for someone else (the ownership check
            // would have matched without the user filter); a plain unknown-id typo is not logged,
            // and neither is a parse that simply never captured an original.
            var exists = await db.ResumeFiles
                .AsNoTracking()
                .AnyAsync(f => f.ParsedResumeId == parsedResumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", query.ParsedResumeId, owner.UserId,
                    "DownloadParsedResumeOriginal");
            }

            return null;
        }

        return ResumeOriginalReader.Open(opener, file);
    }
}
