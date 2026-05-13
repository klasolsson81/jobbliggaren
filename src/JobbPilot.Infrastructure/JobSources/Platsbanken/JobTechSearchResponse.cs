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
