using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionMatchMagnitude;

/// <summary>
/// Owner-scoped load (C-D10, same IDOR posture and probe as <c>BrowseCompaniesQueryHandler</c> —
/// null for unknown AND cross-user), then the port's magnitude count over the shared predicate
/// authority. The register stays behind the port (C-D4 firewall).
/// </summary>
public sealed class GetCriterionMatchMagnitudeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    ICompanyWatchBrowseQuery browse)
    : IQueryHandler<GetCriterionMatchMagnitudeQuery, CriterionMatchMagnitudeDto?>
{
    public async ValueTask<CriterionMatchMagnitudeDto?> Handle(
        GetCriterionMatchMagnitudeQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var userId = currentUser.UserId.Value;
        var criterionId = new CompanyWatchCriterionId(query.CriterionId);

        var criterion = await db.CompanyWatchCriteria
            .AsNoTracking()
            .Where(c => c.Id == criterionId && c.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (criterion is null)
        {
            var existsForSomebodyElse = await db.CompanyWatchCriteria
                .AsNoTracking()
                .AnyAsync(c => c.Id == criterionId, cancellationToken);
            if (existsForSomebodyElse)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "CompanyWatchCriterion", query.CriterionId, userId, "GetCriterionMatchMagnitude");
            }

            return null;
        }

        var magnitude = await browse.CountMatchingCompaniesAsync(
            criterion.Criteria, CriterionMatchMagnitudeDto.Ceiling, cancellationToken);

        return new CriterionMatchMagnitudeDto(
            magnitude, Saturated: magnitude >= CriterionMatchMagnitudeDto.Ceiling);
    }
}
