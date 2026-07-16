using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatchCriteria;

/// <summary>
/// Owner-scoped list, newest first. No id is taken from the caller, so there is no IDOR surface
/// and no probe to log — an unauthenticated request yields the empty list's shape via the
/// fail-closed guard below (the endpoint is auth-gated anyway; this guard is what makes the
/// handler correct WITHOUT the front door, §2.4).
///
/// <para>
/// Materialize-then-map, deliberately: <c>Criteria</c> is the EF-ignored computed VO — it cannot
/// appear in a LINQ-to-entities projection, and the set is ≤ <c>MaxPerUser</c> (20) rows, so
/// mapping in memory is free.
/// </para>
/// </summary>
public sealed class ListCompanyWatchCriteriaQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<ListCompanyWatchCriteriaQuery, IReadOnlyList<CompanyWatchCriterionDto>>
{
    public async ValueTask<IReadOnlyList<CompanyWatchCriterionDto>> Handle(
        ListCompanyWatchCriteriaQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var userId = currentUser.UserId.Value;

        var criteria = await db.CompanyWatchCriteria
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return criteria
            .Select(c => new CompanyWatchCriterionDto(
                c.Id.Value,
                c.Criteria.SniCodes,
                c.Criteria.MunicipalityCodes,
                c.Label,
                c.CreatedAt,
                c.UpdatedAt))
            .ToList();
    }
}
