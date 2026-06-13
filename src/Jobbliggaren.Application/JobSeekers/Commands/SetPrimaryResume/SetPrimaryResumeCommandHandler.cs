using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetPrimaryResume;

public sealed class SetPrimaryResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<SetPrimaryResumeCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        SetPrimaryResumeCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken)
            ?? throw new NotFoundException("JobSeeker hittades inte.");

        var resumeId = new ResumeId(command.ResumeId);

        var resumeBelongsToJobSeeker = await db.Resumes
            .AsNoTracking()
            .AnyAsync(r => r.Id == resumeId && r.JobSeekerId == jobSeeker.Id, cancellationToken);

        if (!resumeBelongsToJobSeeker)
        {
            var existsElsewhere = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (existsElsewhere)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "SetPrimaryResume");
            }
            throw new NotFoundException("CV hittades inte.");
        }

        var result = jobSeeker.SetPrimaryResume(resumeId, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        return Result.Success(jobSeeker.Id.Value);
    }
}
