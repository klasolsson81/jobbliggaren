using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListCompanyWatchCriteria;

/// <summary>
/// GET /api/v1/me/company-watch-criteria — the current user's criteria (the "smarta bevakningar"
/// list on the bevakningar umbrella, CTO Fork G5). Unpaginated by design: the set is hard-capped at
/// <c>CompanyWatchCriterion.MaxPerUser</c> (20) server-side, so there is no unbounded list to page
/// (§5's rule targets unbounded fetches).
/// </summary>
public sealed record ListCompanyWatchCriteriaQuery()
    : IQuery<IReadOnlyList<CompanyWatchCriterionDto>>, IAuthenticatedRequest;

/// <summary>
/// One criterion as the owner sees it: RAW codes + the user's optional label (CTO Fork G6). The
/// human display-labels ("62 IT-tjänster", "Göteborg") are deliberately NOT resolved server-side —
/// the FE already holds the reference tree (G2) and derives them there; a second label authority
/// could only drift (the <c>WatchFilterDto</c> precedent). The codes are the user's own
/// criterion-PII, returned only to their owner over an auth-gated /me route — and never logged
/// (C-D5: returned is not logged).
/// </summary>
public sealed record CompanyWatchCriterionDto(
    Guid Id,
    IReadOnlyList<string> SniCodes,
    IReadOnlyList<string> MunicipalityCodes,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
