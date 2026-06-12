using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.SavedSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.SavedSearches.Commands.CreateSavedSearch;

public sealed class CreateSavedSearchCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<CreateSavedSearchCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        CreateSavedSearchCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("SavedSearch.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        var criteriaResult = SearchCriteria.Create(
            occupationGroup: command.OccupationGroup,
            municipality: command.Municipality,
            region: command.Region,
            employmentType: command.EmploymentType,
            worktimeExtent: command.WorktimeExtent,
            q: command.Q,
            sortBy: command.SortBy);
        if (criteriaResult.IsFailure)
            return Result.Failure<Guid>(criteriaResult.Error);

        var result = SavedSearch.Create(
            jobSeekerId, command.Name, criteriaResult.Value, command.NotificationEnabled, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        db.SavedSearches.Add(result.Value);

        return Result.Success(result.Value.Id.Value);
    }
}
