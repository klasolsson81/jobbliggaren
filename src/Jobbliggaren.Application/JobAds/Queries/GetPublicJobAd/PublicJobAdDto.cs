namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;

/// <summary>
/// One listed job ad as an anonymous reader may see it (ADR 0160). Only a Listed ad has one; an
/// ended or erased ad is a 410 with no body of its own.
/// </summary>
/// <remarks>
/// Structurally anonymous: no user state, no match data, no contact block, no org.nr, no raw
/// payload, no extracted terms. <c>PublicJobAdDtoContractTests</c> pins the exact member set, so a
/// field added here fails a test before it reaches a public page. Kommun, län and occupation group
/// leave as the concept id and its snapshot name (ADR 0171; #2203 S6 for the group; the name is
/// <see langword="null"/> when the snapshot does not know the id). Employment type and worktime
/// extent stay codes: the frontend catalogue names them per locale (ADR 0043 Amendment 2026-06-13).
/// <c>ApplicationUrl</c> is the employer's application page only as <c>PublicApplicationLink</c> admits
/// it, and <see langword="null"/> otherwise (#2214 PR 1b, ADR 0166 D10 trigger 19).
/// </remarks>
public sealed record PublicJobAdDto(
    Guid Id,
    string Title,
    string CompanyName,
    string Description,
    string Url,
    string? ApplicationUrl,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    string? MunicipalityConceptId,
    string? MunicipalityName,
    string? RegionConceptId,
    string? RegionName,
    string? OccupationGroupConceptId,
    string? OccupationGroupName,
    string? EmploymentTypeConceptId,
    string? WorktimeExtentConceptId);
