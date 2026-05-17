using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Domain.JobAds;

namespace JobbPilot.Application.SavedSearches.Queries;

// ADR 0042 Beslut B — Ssyk/Region single→multi (IReadOnlyList; aldrig null
// från VO:t — tom lista = inget filter).
// ADR 0043 (CTO 2026-05-17, Approach A) — SsykLabels/RegionLabels är en
// ADDITIV read-projektion: server-side namn-berikning via ITaxonomyReadModel
// (in-process, O(1) — ingen /taxonomy/labels-endpoint, ingen Beslut D-cap-yta)
// så /sokningar-listan kan visa svenska namn istället för rå concept-id. De
// råa Ssyk/Region-fälten är OFÖRÄNDRADE (ADR 0039 VO-kontrakt orört); labels
// paras med concept-id (TaxonomyLabelDto) → ingen index-misalignment.
public sealed record SavedSearchDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Ssyk,
    IReadOnlyList<string> Region,
    string? Q,
    JobAdSortBy SortBy,
    bool NotificationEnabled,
    DateTimeOffset? LastRunAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TaxonomyLabelDto> SsykLabels,
    IReadOnlyList<TaxonomyLabelDto> RegionLabels);
