using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.RecentJobSearches.Common;

/// <summary>
/// ADR 0060 — a search <see cref="RecentSearchCaptureGate"/> may admit into recent searches: the fields that together
/// are its identity, and the junior flag the gate refuses on. <c>ListJobAdsQuery</c> carries it so the list answers
/// whether its search may be stored, and <c>CaptureRecentSearchCommand</c> carries it to store one (#2285). Record types
/// match the shape through their primary-constructor properties.
/// </summary>
public interface IRecentSearchCandidate
{
    string? Q { get; }
    IReadOnlyList<string>? OccupationGroup { get; }
    IReadOnlyList<string>? Municipality { get; }
    IReadOnlyList<string>? Region { get; }
    IReadOnlyList<string>? EmploymentType { get; }
    IReadOnlyList<string>? WorktimeExtent { get; }

    // #311 PR-2b C1 (ADR 0087 D6) — arbetsgivar-dimensionen (org.nr). PR-2 höll den MEDVETET
    // UTANFÖR detta interface (CONTAINED-scope: employer trådades bara in i live-sök-filtret, ej
    // sök-identiteten) → ListJobAdsQuery.Employer fångades aldrig. Denna PR (2b C1) lägger till den
    // så record-shapen matchar → en committad ?employer=-sökning fångas till RecentJobSearch.
    IReadOnlyList<string>? Employer { get; }

    // #551 PR-D — distans/remote-dimensionen (bool). PR-B höll den MEDVETET UTANFÖR detta interface
    // (CONTAINED-scope: remote trådades bara in i live-sök-filtret + facet-counts, ej sök-identiteten)
    // → ListJobAdsQuery.Remote fångades aldrig. Denna PR (PR-D) lägger till den så record-shapen
    // matchar → en committad ?remote=-sökning fångas till RecentJobSearch (parity Employer PR-2b C1).
    bool Remote { get; }

    // #2199 — the occupation-name picks. #2199 PR 1 read them only to skip capture; PR 2 persists
    // them (occupation_name_list), so a committed ?occupationName= search is captured whole.
    IReadOnlyList<string>? OccupationName { get; }

    // #2261 — the junior IT list as a filter. Read by the capture gate so a junior search's verdict is the gate's own.
    bool JuniorIt { get; }

    JobAdSortBy SortBy { get; }
}
