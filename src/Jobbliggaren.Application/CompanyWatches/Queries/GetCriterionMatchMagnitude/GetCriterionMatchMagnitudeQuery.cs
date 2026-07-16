using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionMatchMagnitude;

/// <summary>
/// The MAGNITUDE of a saved criterion (CTO Fork G3): "roughly how many companies match" — the
/// number the browse headline renders. A SEPARATE query from <c>BrowseCompaniesQuery</c>,
/// deliberately: the browse returns <c>PagedResult</c> (whose <c>TotalCount</c> is a pagination
/// quantity that must never be read as a magnitude — its own doc), and the Api endpoint COMPOSES
/// the two sends (§2.3: complex flows compose from several handlers) rather than overloading one
/// response. Nullable→404 (parity <c>BrowseCompaniesQuery</c>): unknown id and cross-user id are
/// the same answer.
/// </summary>
public sealed record GetCriterionMatchMagnitudeQuery(Guid CriterionId)
    : IQuery<CriterionMatchMagnitudeDto?>, IAuthenticatedRequest;

/// <summary>
/// The honest magnitude: <see cref="Magnitude"/> is exact when <see cref="Saturated"/> is false;
/// when true the truth is "<see cref="Ceiling"/> or more" and the copy MUST say "10 000+", never
/// the bare number (#859: a rendered magnitude must be true).
/// </summary>
public sealed record CriterionMatchMagnitudeDto(int Magnitude, bool Saturated)
{
    /// <summary>
    /// The PRODUCT ceiling (Klas 2026-07-16) — how far the count query counts before declaring
    /// "10 000+". A product-chosen number, deliberately DISTINCT from the pagination cap
    /// (<c>CompanyBrowseCriteria.MaxServableRows</c>): two different questions, two ceilings
    /// (CTO Fork G3). Single-sourced here; call sites never restate it.
    /// </summary>
    public const int Ceiling = 10_000;
}
