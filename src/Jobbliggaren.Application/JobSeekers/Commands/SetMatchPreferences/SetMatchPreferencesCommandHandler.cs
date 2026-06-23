using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;

/// <summary>
/// Sets the current user's match preferences (F4-12, ADR 0076). Mirrors
/// <c>CreateSavedSearchCommandHandler</c>'s auth + owner-scope shape, but loads the
/// JobSeeker TRACKED (it mutates an existing aggregate, rather than Add-ing a new one)
/// so the <c>UnitOfWorkBehavior</c> persists the change. The Domain factory
/// <see cref="MatchPreferences.Create"/> is the single source of the input invariants;
/// its failure is returned as a 400 (the validator is pre-handler defense-in-depth).
/// </summary>
public sealed class SetMatchPreferencesCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<SetMatchPreferencesCommand, Result>
{
    public async ValueTask<Result> Handle(
        SetMatchPreferencesCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        // Map the wire-shape overlay to the Domain VO (the API never binds the Domain type).
        // Create enforces the cap/format/distinct/range/subset invariants (ADR 0079-amendment);
        // null overlay ⇒ honest empty (full-replace clear), parity with the other dimensions.
        // OfType drops any null array element a malformed body bound (e.g. [null]) BEFORE the
        // eager map dereferences it — parity with NormalizeList/NormalizeOccupationExperience's
        // own null guard, so malformed input degrades to honest-empty, never a 500.
        var occupationExperience = command.PreferredOccupationExperience?
            .OfType<OccupationExperienceInput>()
            .Select(e => new OccupationExperience(e.ConceptId, e.Years))
            .ToList();

        var preferencesResult = MatchPreferences.Create(
            preferredOccupationGroups: command.PreferredOccupationGroups,
            preferredRegions: command.PreferredRegions,
            preferredEmploymentTypes: command.PreferredEmploymentTypes,
            preferredMunicipalities: command.PreferredMunicipalities,
            preferredSkills: command.PreferredSkills,
            experienceYears: command.ExperienceYears,
            preferredOccupationExperience: occupationExperience);
        if (preferencesResult.IsFailure)
            return Result.Failure(preferencesResult.Error);

        jobSeeker.UpdateMatchPreferences(preferencesResult.Value, clock);

        return Result.Success();
    }
}
