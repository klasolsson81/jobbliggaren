namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;

/// <summary>
/// One listed job ad as an anonymous reader may see it (ADR 0160). Only a Listed ad has one; an
/// ended or erased ad is a 410 with no body of its own.
/// </summary>
/// <remarks>
/// Structurally anonymous: no user state, no match data, no contact block, no org.nr, no raw
/// payload, no extracted terms. <c>PublicJobAdDtoContractTests</c> pins the exact member set, so a
/// field added here fails a test before it reaches a public page. The concept ids stay codes;
/// whoever renders them owns the labels (ADR 0043).
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
    string? RegionConceptId,
    string? OccupationGroupConceptId,
    string? EmploymentTypeConceptId,
    string? WorktimeExtentConceptId);
