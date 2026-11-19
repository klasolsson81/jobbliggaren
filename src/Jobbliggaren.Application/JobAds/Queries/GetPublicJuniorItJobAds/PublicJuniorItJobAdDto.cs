namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;

/// <summary>
/// One row of the junior IT list (ADR 0186): the catalogue row's six members and the ad's last publication day, which
/// the list sorts on. A flat record of its own, never a widened <c>PublicJobAdSummaryDto</c>, whose six members
/// ADR 0176 pins. <c>PublicJuniorItJobAdDtoContractTests</c> pins these seven.
/// </summary>
public sealed record PublicJuniorItJobAdDto(
    Guid Id,
    string Title,
    string CompanyName,
    string? MunicipalityName,
    string? RegionName,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>A kommun the visitor can narrow the list to, with the number of listed junior IT ads in it.</summary>
public sealed record PublicJuniorItPlaceDto(string MunicipalityConceptId, string Name, int Count);
