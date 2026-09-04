using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;

/// <summary>
/// #1559 — the MAGNITUDE of a saved criterion's ACTIVE ad set: "how many active job ads do the
/// companies this criterion matches have right now". The number the criterion's detail headline
/// renders, and the number that carries the link to the ads themselves.
///
/// <para>
/// A SEPARATE query from <c>BrowseCriterionAdsQuery</c> for the same reason
/// <c>GetCriterionMatchMagnitudeQuery</c> is separate from <c>BrowseCompaniesQuery</c>: the browse
/// returns a <c>PagedResult</c> whose <c>TotalCount</c> is a pagination quantity that must never be
/// read as a magnitude, and the Api endpoint COMPOSES the two sends (§2.3) rather than overloading
/// one response. It is also consumed ALONE, by the detail page, which renders the number without
/// reading a single ad.
/// </para>
///
/// <para>
/// Nullable → 404, parity with every sibling on this aggregate: unknown id and cross-user id are the
/// same answer, so the response is never an existence oracle.
/// </para>
/// </summary>
public sealed record GetCriterionAdMagnitudeQuery(Guid CriterionId)
    : IQuery<CriterionAdMagnitudeDto?>, IAuthenticatedRequest;

/// <summary>
/// The honest ad magnitude: <see cref="Magnitude"/> is exact when <see cref="Saturated"/> is false;
/// when true the truth is "<see cref="Ceiling"/> or more" and the copy MUST say so, never the bare
/// number (#859: a rendered magnitude must be true).
/// </summary>
public sealed record CriterionAdMagnitudeDto(int Magnitude, bool Saturated)
{
    /// <summary>
    /// The PRODUCT ceiling for the AD question — how far the count query counts before declaring
    /// "10 000+".
    ///
    /// <para>
    /// <b>Its own constant, deliberately, even though it currently equals
    /// <c>CriterionMatchMagnitudeDto.Ceiling</c>.</b> That one is Klas's 2026-07-16 answer to "how
    /// many COMPANIES do we render exactly"; this is the answer to "how many ADS". Two questions, two
    /// ceilings is this port's standing doctrine (CTO Fork G3), and one constant serving both would
    /// mean neither could be moved without moving the other. They are equal today because the same
    /// product judgement applies, not because they are the same number.
    /// </para>
    ///
    /// <para>
    /// <b>Measured non-vacuous</b> (dev register + job_ads, 2026-09-04): the broadest bound-legal
    /// criterion matched 39 909 active ads, so saturation is REACHABLE and the "10 000+" arm is live
    /// copy rather than a branch no data can enter; a realistic criterion (SNI 62* seated in
    /// Göteborg — 3 981 companies) matched 167, far under. Re-measure with the query in
    /// <c>CompanyWatchBrowseQuery.AdCountSql</c> rather than quoting these numbers forward: they date.
    /// </para>
    /// </summary>
    public const int Ceiling = 10_000;
}
