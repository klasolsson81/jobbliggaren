using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.AddPreferredSkills;

/// <summary>
/// #2247 — appends the ids the stored set lacks, in the order given, through
/// <c>MatchPreferences.WithSkills</c>, so the domain's own bound and id rules decide (a full set fails with
/// <c>MatchPreferences.TooManySkills</c>). Nothing new to add writes nothing.
/// </summary>
public sealed class AddPreferredSkillsCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<AddPreferredSkillsCommand, Result>
{
    public async ValueTask<Result> Handle(AddPreferredSkillsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        var stored = jobSeeker.MatchPreferences.PreferredSkills;
        var added = command.ConceptIds
            .Where(id => !stored.Contains(id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (added.Count == 0)
            return Result.Success();

        var preferences = jobSeeker.MatchPreferences.WithSkills([.. stored, .. added]);
        if (preferences.IsFailure)
            return Result.Failure(preferences.Error);

        jobSeeker.UpdateMatchPreferences(preferences.Value, clock);

        return Result.Success();
    }
}
