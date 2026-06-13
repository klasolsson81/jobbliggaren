using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Commands.TransitionTo;

public sealed class TransitionToCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<TransitionToCommand, Result>
{
    public async ValueTask<Result> Handle(
        TransitionToCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var appId = new Jobbliggaren.Domain.Applications.ApplicationId(command.ApplicationId);
        var app = await db.Applications
            .FirstOrDefaultAsync(a => a.Id == appId && a.JobSeekerId == jobSeekerId, cancellationToken);

        if (app is null)
        {
            var exists = await db.Applications
                .AsNoTracking()
                .AnyAsync(a => a.Id == appId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Application", appId.Value, currentUser.UserId.Value, "TransitionTo");
            }
            throw new NotFoundException("Ansökan hittades inte.");
        }

        var target = ApplicationStatus.FromName(command.TargetStatus);
        return app.TransitionTo(target, clock);
    }
}
