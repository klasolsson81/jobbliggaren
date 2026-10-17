namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;

/// <summary>
/// One listed job ad as an anonymous reader may see it (ADR 0160). Only a Listed ad has one; an
/// ended or erased ad is a 410 with no body of its own.
/// </summary>
/// <remarks>
/// Structurally anonymous: no user state, no match data, no contact block, no org.nr, no raw
/// payload, no extracted terms. <c>PublicJobAdDtoContractTests</c> pins the exact member set, so a
/// field added here fails a test before it reaches a public page. Kommun and län leave as the
/// concept id and its snapshot name (ADR 0171; the name is <see langword="null"/> when the snapshot
/// does not know the id). Employment type, worktime extent and occupation group stay codes: the
/// frontend catalogue names the first two per locale (ADR 0043 Amendment 2026-06-13), and the third
/// has no public consumer.
/// </remarks>
public sealed record PublicJobAdDto(
    Guid Id,
    string Title,
    string CompanyName,
    string Description,
    string Url,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    string? MunicipalityConceptId,
    string? MunicipalityName,
    string? RegionConceptId,
    string? RegionName,
    string? OccupationGroupConceptId,
    string? EmploymentTypeConceptId,
    string? WorktimeExtentConceptId);
