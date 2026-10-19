using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionNewAdCounts;

/// <summary>
/// ADR 0175 D7 (amended in #1663 PR 2) — "N nya" for each of the caller's Branschbevakningar: the ads Översikt counts
/// and <c>/foretag/branschbevakningar/[id]/nya</c> lists. Its own read, not a field on the criteria list: the list's
/// bucket and Klas's grant 4 are priced on that list's gate statements, and the catalogue page, which reads the list,
/// shows no count. Owner-scoped by <c>ICurrentUser</c>; there is no id parameter.
/// </summary>
public sealed record GetCriterionNewAdCountsQuery() : IQuery<CriterionNewAdCountsDto>, IAuthenticatedRequest;

/// <summary>One entry per criterion the caller has.</summary>
public sealed record CriterionNewAdCountsDto(IReadOnlyList<CriterionNewAdCountDto> Items)
{
    public static readonly CriterionNewAdCountsDto Empty = new([]);
}

/// <summary>
/// <see cref="NewAds"/> is <c>null</c> unless the criterion's last scan answered Scanned — refused as too broad, not
/// materialised, or never scanned (ADR 0120: a number is exact or absent). A real zero is <c>0</c>.
/// </summary>
public sealed record CriterionNewAdCountDto(Guid CriterionId, int? NewAds);
