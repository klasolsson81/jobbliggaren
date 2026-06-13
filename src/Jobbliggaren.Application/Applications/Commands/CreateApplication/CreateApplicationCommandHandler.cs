using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Commands.CreateApplication;

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

        ManualPosting? manualPosting = null;
        if (command.Manual is not null)
        {
            var manualResult = ManualPosting.Create(
                command.Manual.Title,
                command.Manual.Company,
                command.Manual.Url,
                command.Manual.ExpiresAt);
            if (manualResult.IsFailure)
                return Result.Failure<Guid>(manualResult.Error);
            manualPosting = manualResult.Value;
        }

        var result = DomainApplication.Create(
            jobSeekerId, jobAdId, command.CoverLetter, manualPosting, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        db.Applications.Add(result.Value);

        return Result.Success(result.Value.Id.Value);
    }
}
