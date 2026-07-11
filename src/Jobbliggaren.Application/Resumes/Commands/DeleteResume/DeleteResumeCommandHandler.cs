using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Files;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.DeleteResume;

public sealed class DeleteResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<DeleteResumeCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteResumeCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        // jobSeeker hämtas tracked (inte AsNoTracking + Id-select) eftersom
        // cascade-unset av PrimaryResumeId kräver mutation i samma UoW.
        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        var jobSeekerId = jobSeeker?.Id ?? default;

        var resumeId = new ResumeId(command.ResumeId);
        var resume = await db.Resumes
            .Include(r => r.Versions)
            .FirstOrDefaultAsync(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId, cancellationToken);

        if (resume is null)
        {
            var exists = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "DeleteResume");
            }
            throw new NotFoundException("CV hittades inte.");
        }

        resume.SoftDelete(clock);

        // Cascade-konsistens per ADR 0059: om den raderade Resume var primary
        // för JobSeekern → nullas PrimaryResumeId i samma SaveChanges. Manuell
        // cascade per Jobbliggarens etablerade mönster (jfr DeleteAccountCommandHandler)
        // — domain-events har ingen dispatcher i nuvarande infra.
        if (jobSeeker is not null && jobSeeker.PrimaryResumeId == resumeId)
        {
            jobSeeker.UnsetPrimaryResume(clock);
        }

        // Fas 4b PR-9c (ADR 0100 §D5 / ADR 0103; CTO-bind F1=L-B, F3=F3-ii, erasure=immediate
        // hard-delete): cascade the promoted original-file erasure. The link is
        // Resume.SourceParsedResumeId → the file's ParsedResumeId (already indexed). Null for
        // Template/Legacy CVs and for un-backfillable pre-PR-9c rows → cascade skipped (F2
        // residual: those originals stay erasable via account-hard-delete only). Content-free +
        // DEK-free: project ONLY the id, never the multi-MB sealed bytea (§5 minimisation).
        // Remove a key-only stub so the DELETE rides THIS handler's UnitOfWork SaveChanges — one
        // implicit EF transaction, atomic with the soft-delete above (an xmin conflict rolls
        // back BOTH; never a state where the original is gone but the CV survives, or the
        // reverse). Owner-scoped on JobSeekerId as defence-in-depth parity with the IDOR-hardened
        // load above.
        if (resume.SourceParsedResumeId is { } sourceParsedId)
        {
            var fileIds = await db.ResumeFiles
                .Where(f => f.ParsedResumeId == sourceParsedId && f.JobSeekerId == jobSeekerId)
                .Select(f => f.Id)
                .ToListAsync(cancellationToken);

            foreach (var fileId in fileIds)
                db.ResumeFiles.Remove(ResumeFile.DeleteHandle(fileId));
        }

        return Result.Success();
    }
}
