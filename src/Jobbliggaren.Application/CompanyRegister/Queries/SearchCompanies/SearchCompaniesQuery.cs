using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;
using Mediator;

namespace Jobbliggaren.Application.CompanyRegister.Queries.SearchCompanies;

/// <summary>
/// #560 company-search wave — the /foretag/sok page query: one page of ACTIVE register companies
/// matching the (all-optional) axes.
///
/// <para>
/// <b>Returns bare <c>PagedResult&lt;T&gt;</c> — deliberately NOT <c>Result&lt;…&gt;</c>.</b>
/// <c>PagedResultContractTests</c> forbids the wrapper on a paginated query (the response-type
/// identity is load-bearing, and 400s never reach the Result channel): every input error belongs
/// to <c>ValidationBehavior</c>, so <c>SearchCompaniesQueryValidator</c> transports THE single
/// normalizer (<c>CompanyRegisterSearchCriteria.Create</c>) into the pipeline, and by the time
/// the handler runs there is no failure path left. There is no not-found either — an empty page
/// is an honest answer, never a 404.
/// </para>
///
/// <para>
/// No owner scoping — the register is public legal-entity data and the query reads nothing
/// user-owned (the follow-state overlay is a SEPARATE query on a separate endpoint, CTO F3;
/// composing them here would weld the register read to the private follow graph across the
/// DPIA C-D4 firewall). The endpoint is still auth-gated like every app surface.
/// </para>
/// </summary>
public sealed record SearchCompaniesQuery(
    IReadOnlyList<string?>? SniCodes,
    IReadOnlyList<string?>? MunicipalityCodes,
    string? Name,
    string? OrganizationNumber,
    int Page = 1,
    int PageSize = 20)
    : IQuery<PagedResult<CompanyBrowseDto>>, IAuthenticatedRequest
{
    /// <summary>
    /// REDACTED (#883): carries a client-supplied org.nr term (possibly personnummer-shaped —
    /// that is exactly what <c>Create</c> refuses), and the LoggingBehavior prints requests via
    /// <c>ToString()</c>. Pinned by <c>OrgNrRecordLoggingGuardTests</c>.
    /// </summary>
    public override string ToString() =>
        $"SearchCompaniesQuery(sni: {SniCodes?.Count ?? 0}, kommun: {MunicipalityCodes?.Count ?? 0}, "
        + $"name: {(string.IsNullOrWhiteSpace(Name) ? "no" : "yes")}, org.nr redacted, "
        + $"page {Page}/{PageSize})";
}
