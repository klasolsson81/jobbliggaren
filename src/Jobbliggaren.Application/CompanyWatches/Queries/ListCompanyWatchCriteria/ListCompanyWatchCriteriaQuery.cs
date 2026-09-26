using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Jobbliggaren.Application.CompanyWatches.Queries.GetMyMatchingAdCountForCriterion;
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
///
/// <para>
/// <b>#1681 part 2 — <see cref="Ads"/> and <see cref="Matching"/> are the detail page's OWN DTOs,
/// reused rather than re-modelled.</b> Klas asked for *"samma siffror som redan finns på smarta
/// bevakningar"*, and reusing the types is what makes that true of the honesty rules and not only of
/// the digits: the ad magnitude may saturate and may refuse on breadth, or report that nothing has
/// been materialised yet, and the personal count is exact or absent and never carries a "+". A new
/// pair of members here would have needed its own copy of all of that, and a copy is how two surfaces
/// come to disagree about the same watch.
/// </para>
///
/// <para>
/// Neither number is ever a bare <c>0</c> standing in for "we do not know": the three (respectively
/// four) states are explicit on the wire, because a zero the surface cannot distinguish from
/// ignorance is the defect this whole family is written against (ADR 0120).
/// </para>
/// </summary>
public sealed record CompanyWatchCriterionDto(
    Guid Id,
    IReadOnlyList<string> SniCodes,
    IReadOnlyList<string> MunicipalityCodes,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    CriterionAdMagnitudeDto Ads,
    MyMatchingAdCountDto Matching);
