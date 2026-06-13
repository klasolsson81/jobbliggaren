using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Commands.CreateJobAd;

public sealed class CreateJobAdCommandHandler(IAppDbContext db, IDateTimeProvider clock)
    : ICommandHandler<CreateJobAdCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        CreateJobAdCommand command, CancellationToken cancellationToken)
    {
        var companyResult = Company.Create(command.CompanyName);
        if (companyResult.IsFailure)
            return Result.Failure<Guid>(companyResult.Error);

        var sourceResult = JobSource.FromValue(command.Source ?? string.Empty);
        if (sourceResult.IsFailure)
            return Result.Failure<Guid>(sourceResult.Error);

        var jobAdResult = JobAd.Create(
            command.Title,
            companyResult.Value,
            command.Description,
            command.Url,
            sourceResult.Value,
            command.PublishedAt,
            command.ExpiresAt,
            clock);

        if (jobAdResult.IsFailure)
            return Result.Failure<Guid>(jobAdResult.Error);

        db.JobAds.Add(jobAdResult.Value);

        return Result.Success(jobAdResult.Value.Id.Value);
    }
}
