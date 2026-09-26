using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionMatchMagnitude;

/// <summary>
/// Owner-scoped load (C-D10 — null for unknown AND cross-user alike; the posture and its probe live
/// in <see cref="CriterionOwnerScopedLoader"/>), then the port's magnitude count over the shared
/// predicate authority. The register stays behind the port (C-D4 firewall).
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
        var criterion = await CriterionOwnerScopedLoader.LoadForCurrentUserAsync(
            db, currentUser, failedAccessLogger,
            query.CriterionId, CriterionReadOperation.GetCriterionMatchMagnitude, cancellationToken);

        if (criterion is null)
            return null;

        var magnitude = await browse.CountMatchingCompaniesAsync(
            criterion.Criteria, CriterionMatchMagnitudeDto.Ceiling, cancellationToken);

        return new CriterionMatchMagnitudeDto(
            magnitude, Saturated: magnitude >= CriterionMatchMagnitudeDto.Ceiling);
    }
}
