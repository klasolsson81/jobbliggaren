namespace Jobbliggaren.Application.Applications.Queries;

/// <summary>
/// Jobb-metadata-sammanfattning för en ansökan, projicerad i read-vägen.
/// Källa: JobAd-aggregatet (JobAd-kopplad ansökan) ELLER Application.ManualPosting
/// (manuell ansökan). ADR 0048 — in-handler cross-aggregat-read-join.
/// </summary>
public sealed record JobAdSummaryDto(
    Guid? JobAdId,                 // null när källan är ManualPosting (ingen JobAd-rad)
    string Title,
    string Company,
    string? Url,
    string Source,                 // "Platsbanken" | "LinkedIn" | "Manual" (literal)
    DateTimeOffset? PublishedAt,   // J1: null för manuell; ALDRIG Application.CreatedAt
    DateTimeOffset? ExpiresAt);
