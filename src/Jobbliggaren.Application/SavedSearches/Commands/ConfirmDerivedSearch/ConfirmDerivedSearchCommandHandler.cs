using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.SavedSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedSearches.Commands.ConfirmDerivedSearch;

/// <summary>
/// Creates a SavedSearch from the user's confirmed ssyk-4 occupation selection (ADR 0040 Beslut 4).
/// Mirrors <c>CreateSavedSearchCommandHandler</c> but builds via <c>SavedSearch.CreateFromResume</c>
/// (which adds the derived-from-CV provenance event). Deliberately consumes only the command's
/// PLAIN string ids — it does NOT reference the deriver port or its result, so the structural
/// bearing invariant (DerivedSavedSearchInvariantTests) stays green.
/// </summary>
public sealed class ConfirmDerivedSearchCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<ConfirmDerivedSearchCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        ConfirmDerivedSearchCommand command, CancellationToken cancellationToken)
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
            // #311 PR-2b C1: a CV-derived saved search has no employer dimension (it is derived from
            // occupation/location, not a watched org.nr). Employer is empty here by nature, not a
            // deferred seam. The VO/jsonb remain employer-aware for other paths.
            employer: [],
            // #551 PR-D: a CV-derived saved search has no remote/distans dimension (it is derived
            // from occupation/location, not from a ?remote= facet). Remote is false here by nature,
            // not a deferred seam. The VO/jsonb remain remote-aware for other paths.
            remote: false,
            q: command.Q,
            sortBy: command.SortBy);
        if (criteriaResult.IsFailure)
            return Result.Failure<Guid>(criteriaResult.Error);

        var result = SavedSearch.CreateFromResume(
            jobSeekerId, command.Name, criteriaResult.Value,
            command.NotificationEnabled, command.SourceParsedResumeId, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        db.SavedSearches.Add(result.Value);

        return Result.Success(result.Value.Id.Value);
    }
}
