using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.SavedSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedSearches.Commands.CreateSavedSearch;

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
            // #311 PR-2b C1: the SavedSearch WRITE path does not thread employer yet — the command
            // carries no Employer field and there is no FE employer-picker to send one (YAGNI; issue
            // #415 Concern 1 scopes the identity-threading to the RECENT-capture path + the VO/jsonb).
            // The VO + jsonb converter ARE employer-aware, so a future SavedSearch-with-employer write
            // (post-#448/#455 FE) only adds the field here — nothing to migrate. Not a silent-drop:
            // no employer value flows in to drop.
            employer: [],
            // #551 PR-D: same posture as employer above — the SavedSearch WRITE path does not thread
            // remote yet (no command field / no FE distans-toggle in the save-search form; PR-C). The
            // VO + jsonb converter ARE remote-aware, so a future save-with-remote write only sets this
            // true here — nothing to migrate. Not a silent-drop: no remote value flows in to drop.
            remote: false,
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
