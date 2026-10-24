namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;

/// <summary>
/// One row of the public catalogue (ADR 0176 F5), and of the related section under a public job page (ADR 0182): what a
/// job-seeker needs to choose an ad, and nothing else.
/// </summary>
/// <remarks>
/// Six members, pinned by <c>PublicJobAdSummaryDtoContractTests</c>. Never the description or an excerpt (a name in the
/// running text), never the link to Arbetsförmedlingen or its id, never the deadline, a concept id, the org.nr or the raw
/// payload. Kommun and län leave as names only, null where the snapshot does not know the id.
/// </remarks>
public sealed record PublicJobAdSummaryDto(
    Guid Id,
    string Title,
    string CompanyName,
    string? MunicipalityName,
    string? RegionName,
    DateTimeOffset PublishedAt);
