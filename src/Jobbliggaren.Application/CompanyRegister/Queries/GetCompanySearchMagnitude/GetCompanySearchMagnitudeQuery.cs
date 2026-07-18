using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyRegister.Queries.GetCompanySearchMagnitude;

/// <summary>
/// The MAGNITUDE of a search (sibling of <c>GetCriterionMatchMagnitudeQuery</c>, same Fork G3
/// reasoning): "roughly how many companies match these axes" — the number the /foretag/sok
/// headline renders. A SEPARATE query from <c>SearchCompaniesQuery</c> because the page's
/// <c>PagedResult.TotalCount</c> is a pagination quantity that saturates at the servable cap and
/// must never be read as a magnitude; the Api endpoint COMPOSES the two sends (§2.3).
///
/// <para>
/// Same axes, same single normalizer (<c>CompanyRegisterSearchCriteria.Create</c>, transported
/// into the pipeline by <c>GetCompanySearchMagnitudeQueryValidator</c>), and therefore the same
/// bare return type: post-validation there is no failure path and no not-found — a magnitude of
/// zero is an honest answer. Paging members are absent — a magnitude has no page. (Result is
/// reserved for queries whose ErrorKind actually discriminates; here it never would.)
/// </para>
/// </summary>
public sealed record GetCompanySearchMagnitudeQuery(
    IReadOnlyList<string?>? SniCodes,
    IReadOnlyList<string?>? MunicipalityCodes,
    string? Name,
    string? OrganizationNumber)
    : IQuery<CompanySearchMagnitudeDto>, IAuthenticatedRequest
{
    /// <summary>REDACTED (#883) — same argument as <c>SearchCompaniesQuery.ToString</c>.</summary>
    public override string ToString() =>
        $"GetCompanySearchMagnitudeQuery(sni: {SniCodes?.Count ?? 0}, "
        + $"kommun: {MunicipalityCodes?.Count ?? 0}, "
        + $"name: {(string.IsNullOrWhiteSpace(Name) ? "no" : "yes")}, org.nr redacted)";
}

/// <summary>
/// The honest magnitude: <see cref="Magnitude"/> is exact when <see cref="Saturated"/> is false;
/// when true the truth is "<see cref="Ceiling"/> or more" and the copy MUST say "10 000+", never
/// the bare number (#859: a rendered magnitude must be true).
/// </summary>
public sealed record CompanySearchMagnitudeDto(int Magnitude, bool Saturated)
{
    /// <summary>
    /// The PRODUCT ceiling — the same 10 000 Klas ratified for the criterion magnitude
    /// (2026-07-16), carried by this surface's OWN constant (CTO F1: reuse the pattern,
    /// re-implement the code — the two surfaces may diverge by product decision, and a shared
    /// constant would weld them). Call sites never restate it.
    /// </summary>
    public const int Ceiling = 10_000;
}
