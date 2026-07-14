using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;

/// <summary>
/// #560 kriterie-vågen PR-2 — "show me the ACTIVE companies my criterion <paramref name="CriterionId"/>
/// matches". Owner-scoped: the criterion is loaded for the CURRENT user only, and a criterion that
/// does not exist is indistinguishable from one owned by somebody else (both → <c>NotFound</c>; see
/// the handler).
///
/// <para>
/// <b>Returns a NULLABLE <see cref="PagedResult{T}"/>, not <c>Result&lt;PagedResult&lt;T&gt;&gt;</c>
/// — and the distinction is a house rule worth stating</b> (senior-cto-advisor 2026-07-13, overriding
/// the architect's Q6). NotFound is the ONLY error this query can produce: the 401 path throws in
/// <c>AuthorizationBehavior</c> and the 400 path throws in <c>ValidationBehavior</c>, so neither ever
/// reaches the Result channel. A <c>Result&lt;T&gt;</c> here would wrap an error union of cardinality
/// one — ceremony, not error modelling. <c>Result&lt;T&gt;</c> is reserved for queries where
/// <c>ErrorKind</c> genuinely discriminates (compare <c>LookupCompanyQuery</c>, which carries both a
/// Validation refusal and a NotFound). The nullable form is also what <c>PagedResultContractTests</c>
/// requires: <see cref="PagedResult{T}"/> implements <c>IRecentSearchCaptureResponse</c>, so the house
/// has pipeline behaviors keyed on the RESPONSE TYPE's identity with a silent no-op gate — a
/// <c>Result&lt;&gt;</c> wrapper changes that identity and would quietly disconnect the response from
/// them. Parity: <c>RunSavedSearchQuery</c>, the structural sibling.
/// </para>
///
/// <para>
/// The Api endpoint that exposes this lands in PR-3 together with the criterion CRUD and the browse UI
/// (CTO PR-styckning, one change-reason per delivery). It maps <c>null</c> → 404, parity
/// <c>SavedSearchesEndpoints</c>.
/// </para>
/// </summary>
/// <para>
/// The paging bounds are NOT declared here — they live on <c>CompanyBrowseCriteria</c> (the port's
/// input), because the port is what has to honour them: the count query is capped at
/// <c>MaxPage × PageSize</c> so the pager can never advertise a page this validator would reject. The
/// page cap and the count cap are one knowledge piece and are single-sourced accordingly.
/// </para>
public sealed record BrowseCompaniesQuery(Guid CriterionId, int Page, int PageSize)
    : IQuery<PagedResult<CompanyBrowseDto>?>, IAuthenticatedRequest;
