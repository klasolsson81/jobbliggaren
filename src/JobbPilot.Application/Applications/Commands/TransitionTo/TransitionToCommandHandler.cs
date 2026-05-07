using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Commands.TransitionTo;

public sealed class TransitionToCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
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

        var appId = new JobbPilot.Domain.Applications.ApplicationId(command.ApplicationId);
        var app = await db.Applications
            .FirstOrDefaultAsync(a => a.Id == appId && a.JobSeekerId == jobSeekerId, cancellationToken);

        if (app is null)
            throw new NotFoundException("Ansökan hittades inte.");

        var target = ApplicationStatus.FromName(command.TargetStatus);
        return app.TransitionTo(target, clock);
    }
}
