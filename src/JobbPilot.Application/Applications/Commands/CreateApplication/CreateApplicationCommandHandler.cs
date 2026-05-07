using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Commands.CreateApplication;

public sealed class CreateApplicationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<CreateApplicationCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        CreateApplicationCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("Application.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        var jobAdId = command.JobAdId.HasValue
            ? (JobAdId?)new JobAdId(command.JobAdId.Value)
            : null;

        var result = DomainApplication.Create(jobSeekerId, jobAdId, command.CoverLetter, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        db.Applications.Add(result.Value);

        return Result.Success(result.Value.Id.Value);
    }
}
