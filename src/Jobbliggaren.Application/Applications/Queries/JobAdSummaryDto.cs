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
    DateTimeOffset? ExpiresAt,
    // #805-3: the source ad's lifecycle status, frozen from JobAd.Status at read
    // time ("Active" | "Archived" | "Erased" — the Art. 17 tombstone row joins
    // here too, and the FE default-denies anything non-Active). This is the ONLY
    // truthful live/gone signal on the Applications read path — the JobAd
    // soft-delete axis it used to be inferred from (jobAd == null) is GONE: #821
    // retired the dead DeletedAt column and its vacuous query filter, so jobAd is
    // never null for a JobAd-linked application. Status is the sole lifecycle axis.
    //
    // null ⟺ the ManualPosting fallback: a manual application has no JobAd row,
    // hence no snapshot-miss tracking and no archival — so we hold NO basis to
    // claim live or gone, and we claim neither. Never defaulted to "Active";
    // that would be a lie in the payload.
    string? Status);
