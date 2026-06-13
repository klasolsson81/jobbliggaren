using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
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

        return Result.Success();
    }
}
