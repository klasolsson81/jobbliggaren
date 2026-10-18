using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionNewAdCounts;

/// <summary>
/// ADR 0175 D7 — the criteria and their last outcome, then <see cref="NewCriterionAdSet.CountPerCriterionAsync"/>:
/// two owner-scoped statements, the second the grouped count of the one set definition. Ungraded, so no profile is
/// read.
/// </summary>
public sealed class GetCriterionNewAdCountsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetCriterionNewAdCountsQuery, CriterionNewAdCountsDto>
{
    public async ValueTask<CriterionNewAdCountsDto> Handle(
        GetCriterionNewAdCountsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return CriterionNewAdCountsDto.Empty;

        var criteria = await db.CompanyWatchCriteria
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.LastScanOutcome })
            .ToListAsync(cancellationToken);
        if (criteria.Count == 0)
            return CriterionNewAdCountsDto.Empty;

        var counts = await NewCriterionAdSet.CountPerCriterionAsync(db, userId, cancellationToken);

        return new CriterionNewAdCountsDto(criteria
            .Select(c => new CriterionNewAdCountDto(
                c.Id.Value,
                c.LastScanOutcome == CompanyWatchCriterionScanOutcome.Scanned
                    ? (int?)counts.GetValueOrDefault(c.Id)
                    : null))
            .ToList());
    }
}
