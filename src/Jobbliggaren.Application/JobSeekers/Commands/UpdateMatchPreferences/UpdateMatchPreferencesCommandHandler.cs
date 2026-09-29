using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateMatchPreferences;

/// <summary>
/// Lays the request's parts over the stored preferences (ADR 0147). The value object's
/// <c>With*</c> methods own the combining and every invariant; this handler only sequences them,
/// returns the first failure with nothing set, and hands the result to the aggregate once. It reads
/// the row it writes, so an ADR 0146 replay lays the same parts over the competing commit.
/// </summary>
public sealed class UpdateMatchPreferencesCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<UpdateMatchPreferencesCommand, Result>
{
    public async ValueTask<Result> Handle(
        UpdateMatchPreferencesCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        var preferences = jobSeeker.MatchPreferences;
        foreach (var applyPart in PartsOf(command))
        {
            var result = applyPart(preferences);
            if (result.IsFailure)
                return Result.Failure(result.Error);
            preferences = result.Value;
        }

        jobSeeker.UpdateMatchPreferences(preferences, clock);

        return Result.Success();
    }

    private static IEnumerable<Func<MatchPreferences, Result<MatchPreferences>>> PartsOf(
        UpdateMatchPreferencesCommand command)
    {
        if (command.Occupations is { } occupations)
        {
            yield return occupations.PreferredOccupationExperience is null
                ? p => p.WithOccupations(occupations.PreferredOccupationGroups)
                : p => p.WithOccupations(
                    occupations.PreferredOccupationGroups,
                    occupations.PreferredOccupationExperience
                        .OfType<OccupationExperienceInput>()
                        .Select(e => new OccupationExperience(e.ConceptId, e.Years)));
        }

        if (command.Skills is { } skills)
            yield return p => p.WithSkills(skills.PreferredSkills);

        if (command.Locations is { } locations)
            yield return p => p.WithLocations(
                locations.PreferredRegions, locations.PreferredMunicipalities, locations.PreferredRemote);

        if (command.EmploymentTypes is { } employmentTypes)
            yield return p => p.WithEmploymentTypes(employmentTypes.PreferredEmploymentTypes);

        if (command.Experience is { } experience)
            yield return p => p.WithExperienceYears(experience.ExperienceYears);
    }
}
