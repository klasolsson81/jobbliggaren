using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.SavedSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedSearches.Commands.UpdateSavedSearch;

public sealed class UpdateSavedSearchCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<UpdateSavedSearchCommand, Result>
{
    public async ValueTask<Result> Handle(
        UpdateSavedSearchCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("SavedSearch.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure(DomainError.NotFound("SavedSearch", command.Id));

        var savedSearchId = new SavedSearchId(command.Id);

        var savedSearch = await db.SavedSearches
            .Where(s => s.Id == savedSearchId && s.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (savedSearch is null)
        {
            await LogIfCrossTenantAsync(savedSearchId, cancellationToken);
            return Result.Failure(DomainError.NotFound("SavedSearch", command.Id));
        }

        if (command.Criteria is not null)
        {
            var c = command.Criteria;
            var criteriaResult = SearchCriteria.Create(
                occupationGroup: c.OccupationGroup,
                municipality: c.Municipality,
                region: c.Region,
                employmentType: c.EmploymentType,
                worktimeExtent: c.WorktimeExtent,
                // #311 PR-2b C1: SavedSearch WRITE path does not thread employer yet (no command
                // field, no FE picker). The VO/jsonb are employer-aware; adding it here is a future
                // additive change. Not a silent-drop — no employer flows in. (Parity CreateSavedSearch.)
                employer: [],
                q: c.Q,
                sortBy: c.SortBy);
            if (criteriaResult.IsFailure)
                return Result.Failure(criteriaResult.Error);

            var updateResult = savedSearch.UpdateCriteria(criteriaResult.Value, clock);
            if (updateResult.IsFailure)
                return updateResult;
        }

        if (command.Name is not null)
        {
            var renameResult = savedSearch.Rename(command.Name, clock);
            if (renameResult.IsFailure)
                return renameResult;
        }

        if (command.NotificationEnabled is not null)
            savedSearch.SetNotification(command.NotificationEnabled.Value, clock);

        return Result.Success();
    }

    // Failed-access-detection (ADR 0031): skilj "okänt id" från "tillhör annan
    // user" för anomaly-loggning. Klient ser identisk 404/NotFound.
    private async Task LogIfCrossTenantAsync(
        SavedSearchId savedSearchId, CancellationToken cancellationToken)
    {
        var exists = await db.SavedSearches
            .AsNoTracking()
            .AnyAsync(s => s.Id == savedSearchId, cancellationToken);
        if (exists)
        {
            failedAccessLogger.LogCrossUserAttempt(
                "SavedSearch", savedSearchId.Value, currentUser.UserId!.Value,
                "UpdateSavedSearch");
        }
    }
}
