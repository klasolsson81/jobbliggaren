using System.Text.Json.Serialization;

namespace JobbPilot.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Wire-format för JobSearch + JobStream snapshot. Speglar JobTech-API:s shape
/// (web-verifierat 2026-05-12 — `jobsearch.api.jobtechdev.se/search` +
/// `jobstream.api.jobtechdev.se/snapshot`). Refit deserialiserar via
/// System.Text.Json. Intern till Infrastructure — exponeras aldrig genom
/// <see cref="JobbPilot.Application.JobAds.Abstractions.IJobSource"/>.
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
