using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Mediator;

namespace Jobbliggaren.Application.SavedSearches.Queries.RunSavedSearch;

/// <summary>
/// POST /api/v1/saved-searches/{id}/run — kör en sparad sökning och returnerar
/// matchande jobbannonser. ADR 0039 Beslut 2: detta är en QUERY utan
/// skriv-sidoeffekt; last_run_at sätts INTE i Fas 2 (skrivlogiken tillhör
/// Fas 5 notification-cadence). Page/PageSize är runtime-pagination (ej del
/// av SearchCriteria-VO:t). Null returneras vid okänt id eller cross-tenant.
/// </summary>
public sealed record RunSavedSearchQuery(
    Guid Id,
    int Page = 1,
    int PageSize = 20)
    : IQuery<PagedResult<JobAdDto>?>, IAuthenticatedRequest;
