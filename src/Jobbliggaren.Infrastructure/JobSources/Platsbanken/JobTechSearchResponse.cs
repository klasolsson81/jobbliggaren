using System.Text.Json.Serialization;

namespace Jobbliggaren.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Wire-format för JobSearch + JobStream snapshot. Speglar JobTech-API:s shape
/// (web-verifierat 2026-05-12 — `jobsearch.api.jobtechdev.se/search` +
/// `jobstream.api.jobtechdev.se/snapshot`). Refit deserialiserar via
/// System.Text.Json. Intern till Infrastructure — exponeras aldrig genom
/// <see cref="Jobbliggaren.Application.JobAds.Abstractions.IJobSource"/>.
/// </summary>
internal sealed class JobTechSearchResponse
{
    [JsonPropertyName("total")]
    public JobTechTotal? Total { get; set; }

    [JsonPropertyName("hits")]
    public List<JobTechHit> Hits { get; set; } = [];
}

internal sealed class JobTechTotal
{
    [JsonPropertyName("value")]
    public int Value { get; set; }
}

internal sealed class JobTechHit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("headline")]
    public string? Headline { get; set; }

    [JsonPropertyName("description")]
    public JobTechDescription? Description { get; set; }

    [JsonPropertyName("employer")]
    public JobTechEmployer? Employer { get; set; }

    // v2-shape (web-verifierat 2026-05-13 mot riktig JobStream 2.1.1).
    // Top-level URL till annonsen på arbetsformedlingen.se/platsbanken.
    // Ersätter v1:s source_links[0].url.
    [JsonPropertyName("webpage_url")]
    public string? WebpageUrl { get; set; }

    // v1-bakåtkompatibilitet: source_links används inte i v2 men hålls för
    // defensiv deserialisering om legacy JobTech-deployment återaktiveras.
    [JsonPropertyName("source_links")]
    public List<JobTechSourceLink>? SourceLinks { get; set; }

    [JsonPropertyName("application_details")]
    public JobTechApplicationDetails? ApplicationDetails { get; set; }

    [JsonPropertyName("publication_date")]
    public DateTimeOffset? PublicationDate { get; set; }

    [JsonPropertyName("last_publication_date")]
    public DateTimeOffset? LastPublicationDate { get; set; }

    [JsonPropertyName("removed")]
    public bool? Removed { get; set; }

    [JsonPropertyName("removed_date")]
    public DateTimeOffset? RemovedDate { get; set; }

    // F6 P4 sök-infrastruktur-fix 2026-05-20 (CTO + dotnet-architect).
    // ROTORSAK till filter-bugg (ssyk/region ger 0 träffar för alla picker-
    // conceptIds): JobTechHit deserialiserade tidigare inte klassifikations-
    // fälten → JsonSerializer.Serialize(hit) i PlatsbankenJobSource.cs:184
    // producerade payload utan occupation/workplace_address → generated
    // columns (JobAdConfiguration ssyk_concept_id/region_concept_id) blev
    // NULL på alla 51k+ rader → ssyk/region-filter alltid 0 träffar.
    //
    // Top-level enligt JobTech v2 jobsearch + jobstream-schema (web-
    // verifierat 2026-05-12 per JobTechPayloadSanitizer §13). occupation_
    // group/occupation_field är top-level, EJ nested under occupation.
    // Sanitizer-allowlist (JobTechPayloadSanitizer.cs:42-50) bevarar redan
    // dessa keys → no-op-passering. Generated columns konsumerar:
    //   - raw_payload->'occupation'->>'concept_id'
    //   - raw_payload->'workplace_address'->>'region_concept_id'
    [JsonPropertyName("occupation")]
    public JobTechOccupation? Occupation { get; set; }

    [JsonPropertyName("occupation_group")]
    public JobTechOccupationGroup? OccupationGroup { get; set; }

    [JsonPropertyName("occupation_field")]
    public JobTechOccupationField? OccupationField { get; set; }

    [JsonPropertyName("workplace_address")]
    public JobTechWorkplaceAddress? WorkplaceAddress { get; set; }

    // B2 / Klass 2 (ADR 0067 Beslut 2, Platsbanken sök-paritet) — anställningsform
    // + omfattning. Båda TOP-LEVEL i JobTech v2-payloaden (live-verifierat
    // 2026-06-08: jobsearch.api.jobtechdev.se/search → employment_type +
    // working_hours_type top-level, samma som occupation_group). POCO:n
    // deserialiserade dem INTE tidigare → JsonSerializer.Serialize(hit) i
    // PlatsbankenJobSource.cs producerade raw_payload UTAN dessa keys → generated
    // columns employment_type_concept_id / worktime_extent_concept_id blev NULL
    // på alla rader tills detta POCO-tillägg + full re-ingest (samma rotorsaks-
    // mönster som F6 P4 fixade för occupation/workplace_address).
    //
    // NAMNGLAPP: payload-fältet heter working_hours_type men taxonomi-concept-
    // typen (och STORED-kolumnen) heter worktime-extent/worktime_extent
    // (omfattning, Heltid/Deltid) — exakt som occupation_group→ssyk-level-4 i B1.
    // POCO-property:n följer wire-formatet (working_hours_type); översättningen
    // till kolumn-namn sker i JobAdConfiguration (shadow-property-mapping).
    [JsonPropertyName("employment_type")]
    public JobTechEmploymentType? EmploymentType { get; set; }

    [JsonPropertyName("working_hours_type")]
    public JobTechWorkingHoursType? WorkingHoursType { get; set; }
}

internal sealed class JobTechDescription
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal sealed class JobTechEmployer
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class JobTechSourceLink
{
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

internal sealed class JobTechApplicationDetails
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

// F6 P4 — JobTech v2-klassifikation. Tre concept-typer (occupation/
// occupation_group/occupation_field) delar shape; separata typer för
// läsbarhet och divergens-tålighet (JobTech kan ändra var och en oberoende).

internal sealed class JobTechOccupation
{
    [JsonPropertyName("concept_id")]
    public string? ConceptId { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("legacy_ams_taxonomy_id")]
    public string? LegacyAmsTaxonomyId { get; set; }
}

internal sealed class JobTechOccupationGroup
{
    [JsonPropertyName("concept_id")]
    public string? ConceptId { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("legacy_ams_taxonomy_id")]
    public string? LegacyAmsTaxonomyId { get; set; }
}

internal sealed class JobTechOccupationField
{
    [JsonPropertyName("concept_id")]
    public string? ConceptId { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("legacy_ams_taxonomy_id")]
    public string? LegacyAmsTaxonomyId { get; set; }
}

// B2 / Klass 2 — anställningsform + omfattning. Samma {concept_id, label,
// legacy_ams_taxonomy_id}-shape som occupation-trion; separata typer för
// divergens-tålighet (JobTech kan ändra var och en oberoende). Klass-namnet
// WorkingHoursType speglar wire-keyn (working_hours_type), INTE taxonomi-typen
// worktime-extent — POCO är ACL mot JobTech-wire-formatet (Evans 2003 §14).

internal sealed class JobTechEmploymentType
{
    [JsonPropertyName("concept_id")]
    public string? ConceptId { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("legacy_ams_taxonomy_id")]
    public string? LegacyAmsTaxonomyId { get; set; }
}

internal sealed class JobTechWorkingHoursType
{
    [JsonPropertyName("concept_id")]
    public string? ConceptId { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("legacy_ams_taxonomy_id")]
    public string? LegacyAmsTaxonomyId { get; set; }
}

// JobTech v2 workplace_address — region/municipality/country med både
// concept_id (taxonomi-pekare) och label (mänskligt läsbar). Generated
// column raw_payload->'workplace_address'->>'region_concept_id' projiserar
// till shadow-property RegionConceptId (JobAdConfiguration.cs:78-80).
internal sealed class JobTechWorkplaceAddress
{
    [JsonPropertyName("region_concept_id")]
    public string? RegionConceptId { get; set; }

    [JsonPropertyName("municipality_concept_id")]
    public string? MunicipalityConceptId { get; set; }

    [JsonPropertyName("country_concept_id")]
    public string? CountryConceptId { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("municipality")]
    public string? Municipality { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}
