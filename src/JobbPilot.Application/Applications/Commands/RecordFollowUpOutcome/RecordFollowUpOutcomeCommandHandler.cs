using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Commands.RecordFollowUpOutcome;

public sealed class RecordFollowUpOutcomeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<RecordFollowUpOutcomeCommand, Result>
{
    public async ValueTask<Result> Handle(
        RecordFollowUpOutcomeCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var appId = new JobbPilot.Domain.Applications.ApplicationId(command.ApplicationId);
        var app = await db.Applications
            .Include(a => a.FollowUps)
            .FirstOrDefaultAsync(a => a.Id == appId && a.JobSeekerId == jobSeekerId, cancellationToken);

        if (app is null)
        {
            var exists = await db.Applications
                .AsNoTracking()
                .AnyAsync(a => a.Id == appId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Application", appId.Value, currentUser.UserId.Value, "RecordFollowUpOutcome");
            }
            throw new NotFoundException("Ansökan hittades inte.");
        }

        var outcome = FollowUpOutcome.FromName(command.Outcome);
        var followUpId = new FollowUpId(command.FollowUpId);
        return app.RecordFollowUpOutcome(followUpId, outcome, clock);
    }
}
