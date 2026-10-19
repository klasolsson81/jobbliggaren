using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionNewAdCounts;

/// <summary>
/// ADR 0175 D7 — the criteria. Ungraded, so no profile is read.
/// </summary>
public sealed class GetCriterionNewAdCountsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetCriterionNewAdCountsQuery, CriterionNewAdCountsDto>
{
    public async ValueTask<CriterionNewAdCountsDto> Handle(
        GetCriterionNewAdCountsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return CriterionNewAdCountsDto.Empty;

        var counts = NewCriterionAdSet.NewHits(db, userId)
            .GroupBy(h => h.CompanyWatchCriterionId)
            .Select(g => new { Id = g.Key, Count = g.Count() });
        var criteria = await db.CompanyWatchCriteria
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(NewCriterionAdSet.CurrentOutcome)
            .LeftJoin(
                counts,
                c => c.Id,
                n => n.Id,
                (c, n) => new
                {
                    c.Id,
                    NewAds = c.Outcome == CompanyWatchCriterionScanOutcome.Scanned
                        ? ((int?)n!.Count ?? 0)
                        : (int?)null,
                })
            .ToListAsync(cancellationToken);
        if (criteria.Count == 0)
            return CriterionNewAdCountsDto.Empty;

        return new CriterionNewAdCountsDto(criteria
            .Select(c => new CriterionNewAdCountDto(c.Id.Value, c.NewAds))
            .ToList());
    }
}
