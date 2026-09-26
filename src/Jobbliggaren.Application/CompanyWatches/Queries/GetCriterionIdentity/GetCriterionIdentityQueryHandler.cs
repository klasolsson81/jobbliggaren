using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionIdentity;

/// <summary>
/// #1681 part 2 — owner-scoped load (<see cref="CriterionOwnerScopedLoader"/> carries the ADR 0031
/// posture and its cross-user probe), then a projection of what the aggregate already holds.
///
/// <para>
/// <b>It reads nothing else, and that is the entire feature.</b> No port, no register, no
/// <c>job_ads</c>, no grading — one primary-key load of a row the caller has proven it owns. The
/// heading it serves used to cost the whole criteria list plus its materialised counts.
/// </para>
/// </summary>
public sealed class GetCriterionIdentityQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetCriterionIdentityQuery, CriterionIdentityDto?>
{
    public async ValueTask<CriterionIdentityDto?> Handle(
        GetCriterionIdentityQuery query, CancellationToken cancellationToken)
    {
        var criterion = await CriterionOwnerScopedLoader.LoadForCurrentUserAsync(
            db, currentUser, failedAccessLogger,
            query.CriterionId, CriterionReadOperation.GetCriterionIdentity, cancellationToken);

        if (criterion is null)
            return null;

        return new CriterionIdentityDto(
            criterion.Id.Value,
            criterion.Criteria.SniCodes,
            criterion.Criteria.MunicipalityCodes,
            criterion.Label);
    }
}
