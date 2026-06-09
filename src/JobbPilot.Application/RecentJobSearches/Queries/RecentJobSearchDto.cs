using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Domain.JobAds;

namespace JobbPilot.Application.RecentJobSearches.Queries;

/// <summary>
/// ADR 0060 — read-projection för /sokningar-listan + Senaste-hero-chip.
/// Server-side label-resolve via <see cref="ITaxonomyReadModel"/> (paritet
/// med SavedSearchDto). <see cref="Label"/> server-härleds från Q eller
/// första label för UI-affordance utan FE-logik. <see cref="CurrentCount"/>
/// är live-räknat per row (cap=20 håller N+1 under kontroll, CTO Variant A).
/// <see cref="NewCount"/> = <c>max(0, CurrentCount - LastSeenCount)</c>.
///
/// <para><b>Fas C2 (ADR 0067, architect F5 2026-06-09) — ADDITIV form:</b>
/// <see cref="SsykList"/>/<see cref="SsykLabels"/> är <b>deprecated — alltid
/// tomma sedan C2</b> (occupation-name-dimensionen utgick ur entiteten) men
/// behålls i wire-kontraktet: FE-zod-schemat (recent-searches.ts) har
/// <c>ssykList</c> REQUIRED och C2 får inte röra FE. Nya fält tillkommer SIST
/// (zod stripper okända nycklar → osynliga för FE tills Fas E). Tas bort i
/// Fas E tillsammans med FE-zod-schemat + ?ssyk=→?occupationGroup=-bytet.</para>
/// </summary>
public sealed record RecentJobSearchDto(
    Guid Id,
    string? Q,
    IReadOnlyList<string> SsykList,
    IReadOnlyList<string> RegionList,
    IReadOnlyList<TaxonomyLabelDto> SsykLabels,
    IReadOnlyList<TaxonomyLabelDto> RegionLabels,
    JobAdSortBy SortBy,
    string Label,
    int CurrentCount,
    int NewCount,
    DateTimeOffset LastViewedAt,
    IReadOnlyList<string> OccupationGroupList,
    IReadOnlyList<string> MunicipalityList,
    IReadOnlyList<TaxonomyLabelDto> OccupationGroupLabels,
    IReadOnlyList<TaxonomyLabelDto> MunicipalityLabels);
