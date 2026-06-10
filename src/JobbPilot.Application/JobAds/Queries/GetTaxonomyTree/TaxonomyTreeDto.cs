using JobbPilot.Application.JobAds.Abstractions;

namespace JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;

/// <summary>
/// Picker-träd (ADR 0043). Rena Application-DTOs (CLAUDE.md §3.3 record class).
/// Namn visas i UI; concept-id är vad väljaren emitterar till URL/VO
/// (oförändrat kontrakt, ADR 0043 Beslut E).
/// <para>
/// ADR 0067 Beslut 1 + ADR 0043-amendment 2026-06-08 (Platsbanken sök-paritet
/// Fas C1) — additiv kaskad: Län→Kommun + Yrkesområde→Yrkesgrupp (ssyk-level-4)
/// exponeras (B1 seedade noderna, B1-CTO Beslut 2 sköt DTO-exponeringen hit).
/// Speglar Platsbankens två-nivå-kaskad-pickers. Befintliga occupation-name-yrken
/// (<see cref="TaxonomyOccupationFieldDto.Occupations"/>) BEHÅLLS additivt
/// (Open-Closed; occupation-name-substrat för recall + CV-matchning TD-93).
/// </para>
/// </summary>
public sealed record TaxonomyTreeDto(
    IReadOnlyList<TaxonomyRegionDto> Regions,
    IReadOnlyList<TaxonomyOccupationFieldDto> OccupationFields);

/// <summary>Län (JobTech <c>region</c>, ~21) med underordnade kommuner
/// (ADR 0043-amendment 2026-06-08).</summary>
public sealed record TaxonomyRegionDto(
    string ConceptId,
    string Label,
    IReadOnlyList<TaxonomyMunicipalityDto> Municipalities);

/// <summary>Kommun (JobTech <c>municipality</c>, ~290). Concept-id matchar
/// <c>job_ads.municipality_concept_id</c>.</summary>
public sealed record TaxonomyMunicipalityDto(string ConceptId, string Label);

/// <summary>Yrkesområde (JobTech <c>occupation-field</c>, ~21) med
/// underordnade yrkesgrupper (ssyk-level-4, primärt yrke-filter) och yrken
/// (occupation-name, bevarat recall-/CV-substrat).</summary>
public sealed record TaxonomyOccupationFieldDto(
    string ConceptId,
    string Label,
    IReadOnlyList<TaxonomyOccupationDto> Occupations,
    IReadOnlyList<TaxonomyOccupationGroupDto> OccupationGroups);

/// <summary>Yrke (JobTech <c>occupation-name</c>). Concept-id matchar
/// <c>job_ads.ssyk_concept_id</c> (= <c>raw_payload-&gt;occupation-&gt;concept_id</c>).
/// Bevaras som synonym-/recall-substrat (ADR 0067 Beslut 1) — ej längre primärt
/// yrke-filter.</summary>
public sealed record TaxonomyOccupationDto(string ConceptId, string Label);

/// <summary>Yrkesgrupp (JobTech <c>ssyk-level-4</c>, ~400). Concept-id matchar
/// <c>job_ads.occupation_group_concept_id</c> — primärt yrke-filter för
/// Platsbanken-paritet (ADR 0067 Beslut 1).</summary>
public sealed record TaxonomyOccupationGroupDto(string ConceptId, string Label);

/// <summary>Reverse-lookup-rad: concept-id → visningsnamn (eller
/// fallback för okänt id).</summary>
public sealed record TaxonomyLabelDto(string ConceptId, string Label);

/// <summary>
/// Ett taxonomi-prefix-förslag (ADR 0067 Beslut 5a) returnerat av
/// <see cref="JobbPilot.Application.JobAds.Abstractions.ITaxonomyReadModel.SuggestByPrefixAsync"/>.
/// Porten översätter Infrastructures interna <c>TaxonomyConceptKind</c> till den
/// publika <see cref="SuggestionKind"/> (ACL — concept-id-vokabulär läcker aldrig
/// uppåt; <c>TaxonomyConceptKind</c> är <c>internal</c>). <see cref="ConceptId"/>
/// är alltid satt (taxonomi-noder har alltid concept-id).
/// </summary>
public sealed record TaxonomySuggestionDto(SuggestionKind Kind, string ConceptId, string Label);
