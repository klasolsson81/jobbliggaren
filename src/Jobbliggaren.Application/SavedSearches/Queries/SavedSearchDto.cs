using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.SavedSearches.Queries;

// ADR 0042 Beslut B — multi-värde-listor (IReadOnlyList; aldrig null från
// VO:t — tom lista = inget filter).
// ADR 0043 (CTO 2026-05-17, Approach A) — *Labels är en ADDITIV read-
// projektion: server-side namn-berikning via ITaxonomyReadModel (in-process,
// O(1)) så /sokningar-listan kan visa svenska namn istället för rå concept-id.
// Labels paras med concept-id (TaxonomyLabelDto) → ingen index-misalignment.
// ADR 0067 Fas C2 (CTO-dom (e)/(f), architect F5.5): Ssyk/SsykLabels UTGICK —
// SavedSearch-API:t konsumeras inte av FE (ADR 0039-amendment 2026-05-20) →
// DTO:n renamead fritt till kanonisk dimensionsordning (OccupationGroup,
// Municipality, Region; architect F1), labels per dimension sist.
// ADR 0067 Beslut 6 (Fas B2, 2026-06-12): EmploymentType + WorktimeExtent
// (Klass 2) tillkom som råa listor. MEDVETET UTAN *Labels — taxonomi-
// reverse-lookup för anställningsform/omfattning är ett Fas E presentations-
// concern (ITaxonomyReadModel bär occupation/geo-trädet, ej Klass 2). De råa
// listorna räcker för att reproducera filtret (RunSavedSearch).
public sealed record SavedSearchDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> OccupationGroup,
    IReadOnlyList<string> Municipality,
    IReadOnlyList<string> Region,
    IReadOnlyList<string> EmploymentType,
    IReadOnlyList<string> WorktimeExtent,
    string? Q,
    JobAdSortBy SortBy,
    bool NotificationEnabled,
    DateTimeOffset? LastRunAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TaxonomyLabelDto> OccupationGroupLabels,
    IReadOnlyList<TaxonomyLabelDto> MunicipalityLabels,
    IReadOnlyList<TaxonomyLabelDto> RegionLabels);
