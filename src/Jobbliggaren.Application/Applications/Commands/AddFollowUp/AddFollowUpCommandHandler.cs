using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Commands.AddFollowUp;

public sealed class AddFollowUpCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<AddFollowUpCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        AddFollowUpCommand command, CancellationToken cancellationToken)
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
                    "Application", appId.Value, currentUser.UserId.Value, "AddFollowUp");
            }
            throw new NotFoundException("Ansökan hittades inte.");
        }

        var channel = FollowUpChannel.FromName(command.Channel);
        var result = app.AddFollowUp(channel, command.ScheduledAt, command.Note, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        return Result.Success(result.Value.Value);
    }
}
