using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.RecentJobSearches.Queries;

/// <summary>
/// ADR 0060 — read-projection för /sokningar-listan + Senaste-hero-chip.
/// Server-side label-resolve via <see cref="ITaxonomyReadModel"/> (paritet
/// med SavedSearchDto). <see cref="Label"/> server-härleds från Q eller
/// första label för UI-affordance utan FE-logik. <see cref="CurrentCount"/>
/// är live-räknat per row (cap=20 håller N+1 under kontroll, CTO Variant A).
/// <see cref="NewCount"/> = <c>max(0, CurrentCount - LastSeenCount)</c>.
///
/// <para><b>Fas E2b (ADR 0067, CTO-direktiv commit 3 2026-06-11):</b>
/// C2-shimmets deprecated alltid-tomma <c>SsykList</c>/<c>SsykLabels</c> är
/// BORTTAGNA — FE-zod-schemat frikopplades från <c>ssykList</c> i E2a
/// (architect F5: "tas bort i Fas E"). Dimensionerna ordnas yrkesgrupp →
/// kommun → region (samma ordning som filter-SPOT:en); wire-kontraktet är
/// namnbaserat (camelCase, zod) så positionsordningen är intern.</para>
/// </summary>
// ADR 0067 Beslut 6 (Fas B2, 2026-06-12): EmploymentTypeList + WorktimeExtentList
// (Klass 2) tillkom som råa listor, MEDVETET UTAN *Labels (Fas E presentations-
// concern — implementerings-notatet 2026-06-12 ordagrant).
// De råa listorna säkerställer att CountAsync filtrerar på exakt samma kriterium
// som sökningen och att en framtida re-run reproducerar Klass 2.
//
// #1407: Remote surfas, Employer surfas inte. Vilka dimensioner som når projektionen,
// och varför en utelämnas, ägs av RecentJobSearchProjectionParityTests — ett hem, som
// fäller testet i stället för att bara beskriva.
public sealed record RecentJobSearchDto(
    Guid Id,
    string? Q,
    IReadOnlyList<string> OccupationGroupList,
    IReadOnlyList<string> MunicipalityList,
    IReadOnlyList<string> RegionList,
    IReadOnlyList<string> EmploymentTypeList,
    IReadOnlyList<string> WorktimeExtentList,
    bool Remote,
    IReadOnlyList<TaxonomyLabelDto> OccupationGroupLabels,
    IReadOnlyList<TaxonomyLabelDto> MunicipalityLabels,
    IReadOnlyList<TaxonomyLabelDto> RegionLabels,
    JobAdSortBy SortBy,
    string Label,
    int CurrentCount,
    int NewCount,
    DateTimeOffset LastViewedAt);
