using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.DeleteResumeVersion;

public sealed class DeleteResumeVersionCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<DeleteResumeVersionCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteResumeVersionCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

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
                    "Resume", resumeId.Value, currentUser.UserId.Value, "DeleteResumeVersion");
            }
            throw new NotFoundException("CV hittades inte.");
        }

        // F4-11 (BUILD §5.6): a version cannot be deleted while a NON-TERMINAL
        // application references it. "Non-terminal" = Status ∉ {Accepted, Rejected,
        // Withdrawn}; Ghosted is reactivatable and therefore blocks deletion. The
        // three terminals are listed explicitly — a SmartEnum property does not
        // translate to SQL (the Status column is the converted .Name string). The
        // soft-deleted-application query filter on db.Applications applies here.
        var versionId = new ResumeVersionId(command.VersionId);
        var isReferencedByOpenApplication = await db.Applications
            .AsNoTracking()
            .AnyAsync(
                a => a.ResumeVersionId == versionId
                  && a.Status != ApplicationStatus.Accepted
                  && a.Status != ApplicationStatus.Rejected
                  && a.Status != ApplicationStatus.Withdrawn,
                cancellationToken);

        return resume.DeleteVersion(versionId, isReferencedByOpenApplication, clock);
    }
}
