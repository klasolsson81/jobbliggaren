namespace JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;

/// <summary>
/// Picker-träd (ADR 0043). Rena Application-DTOs (CLAUDE.md §3.3 record class).
/// Namn visas i UI; concept-id är vad väljaren emitterar till URL/VO
/// (oförändrat kontrakt, ADR 0043 Beslut E). Variant A-scope: Län + Yrke.
/// </summary>
public sealed record TaxonomyTreeDto(
    IReadOnlyList<TaxonomyRegionDto> Regions,
    IReadOnlyList<TaxonomyOccupationFieldDto> OccupationFields);

/// <summary>Län (JobTech <c>region</c>, ~21). Enkelnivå — ingen kommun
/// (ADR 0043 payload-verifierings-trigger).</summary>
public sealed record TaxonomyRegionDto(string ConceptId, string Label);

/// <summary>Yrkesområde (JobTech <c>occupation-field</c>, ~21) med
/// underordnade yrken.</summary>
public sealed record TaxonomyOccupationFieldDto(
    string ConceptId,
    string Label,
    IReadOnlyList<TaxonomyOccupationDto> Occupations);

/// <summary>Yrke (JobTech <c>occupation-name</c>). Concept-id matchar
/// <c>job_ads.ssyk_concept_id</c> (= <c>raw_payload-&gt;occupation-&gt;concept_id</c>).</summary>
public sealed record TaxonomyOccupationDto(string ConceptId, string Label);

/// <summary>Reverse-lookup-rad: concept-id → visningsnamn (eller
/// fallback för okänt id).</summary>
public sealed record TaxonomyLabelDto(string ConceptId, string Label);
