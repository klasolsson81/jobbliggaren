using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
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

        // TODO(Fas 4): När Application-aggregatet får ResumeVersionId-fält ska denna
        // fråga slå upp om versionen är refererad av en icke-terminal Application
        // (BUILD.md §5.6 invariant). Just nu finns inget sådant fält, så svaret är
        // alltid false.
        const bool isReferencedByOpenApplication = false;

        var versionId = new ResumeVersionId(command.VersionId);
        return resume.DeleteVersion(versionId, isReferencedByOpenApplication, clock);
    }
}
