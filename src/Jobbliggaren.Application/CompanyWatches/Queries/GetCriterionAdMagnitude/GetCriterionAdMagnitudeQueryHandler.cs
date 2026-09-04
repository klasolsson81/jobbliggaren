using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;

/// <summary>
/// #1559 — owner-scoped load (<see cref="CriterionOwnerScopedLoader"/> carries the ADR 0031 posture
/// and its cross-user probe), then the port's capped ad count over the shared register predicate
/// joined to <c>job_ads</c>.
///
/// <para>
/// The join itself cannot happen here and that is structural, not stylistic: <c>company_register</c>
/// is not a <c>DbSet</c> on <see cref="IAppDbContext"/> (DPIA C-D4 / M-C5, fail-closed architecture
/// test), so this handler can read the user's criterion and nothing else. The register only ever
/// answers through the port.
/// </para>
///
/// <para>
/// <b>Counts-only (DPIA C-D5).</b> The port returns an <c>int</c>. No org.nr and no company name
/// crosses this handler at all, which is why the number is safe to surface even for a criterion whose
/// matches include sole traders.
/// </para>
/// </summary>
public sealed class GetCriterionAdMagnitudeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    ICompanyWatchBrowseQuery browse)
    : IQueryHandler<GetCriterionAdMagnitudeQuery, CriterionAdMagnitudeDto?>
{
    public async ValueTask<CriterionAdMagnitudeDto?> Handle(
        GetCriterionAdMagnitudeQuery query, CancellationToken cancellationToken)
    {
        var criterion = await CriterionOwnerScopedLoader.LoadForCurrentUserAsync(
            db, currentUser, failedAccessLogger,
            query.CriterionId, CriterionReadOperation.GetCriterionAdMagnitude, cancellationToken);

        if (criterion is null)
            return null;

        var magnitude = await browse.CountActiveAdsAsync(
            criterion.Criteria, CriterionAdMagnitudeDto.Ceiling, cancellationToken);

        return new CriterionAdMagnitudeDto(
            magnitude, Saturated: magnitude >= CriterionAdMagnitudeDto.Ceiling);
    }
}
