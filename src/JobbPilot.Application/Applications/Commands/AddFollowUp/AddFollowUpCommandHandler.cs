using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Commands.AddFollowUp;

public sealed class AddFollowUpCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
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

        var appId = new JobbPilot.Domain.Applications.ApplicationId(command.ApplicationId);
        var app = await db.Applications
            .FirstOrDefaultAsync(a => a.Id == appId && a.JobSeekerId == jobSeekerId, cancellationToken);

        if (app is null)
            throw new NotFoundException("Ansökan hittades inte.");

        var channel = FollowUpChannel.FromName(command.Channel);
        var result = app.AddFollowUp(channel, command.ScheduledAt, command.Note, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        return Result.Success(result.Value.Value);
    }
}
