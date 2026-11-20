using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.JuniorJobAds.Commands.DecideJuniorJobAd;

/// <summary>
/// #2264 (ADR 0187) — loads the ad's assessment tracked and decides through it. Only an assessed ad that is listed now
/// and in an enabled field can be decided: the queue shows nothing else, and a decision about a closed, erased or manual
/// ad answers nothing. Eligibility is time-relative (<c>JobAdPublication.IsListed(now)</c>), so it is checked here,
/// in the load, and not inside the aggregate (senior-cto-advisor, 2026-11-20).
/// </summary>
public sealed class DecideJuniorJobAdCommandHandler(
    IAppDbContext db,
    IJuniorFieldCatalog catalog,
    IDateTimeProvider clock)
    : ICommandHandler<DecideJuniorJobAdCommand, Result>
{
    public async ValueTask<Result> Handle(DecideJuniorJobAdCommand command, CancellationToken cancellationToken)
    {
        var id = new JobAdId(command.JobAdId);
        var groups = JuniorMembership.GroupsOf(await catalog.ResolveEnabledAsync(cancellationToken)).ToList();
        var eligibleAds = db.JobAds
            .Where(JobAdPublication.IsListed(clock.UtcNow))
            .Where(j => j.OccupationGroupConceptId != null && groups.Contains(j.OccupationGroupConceptId));

        var assessment = await db.JuniorAssessments
            .Where(a => a.Id == id && eligibleAds.Any(j => j.Id == a.Id))
            .FirstOrDefaultAsync(cancellationToken);

        if (assessment is null)
            return Result.Failure(DomainError.NotFound("JobAd", command.JobAdId));

        return assessment.Decide(command.Decision, clock);
    }
}
