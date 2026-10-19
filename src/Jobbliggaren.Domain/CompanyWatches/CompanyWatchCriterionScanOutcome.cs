namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// ADR 0175 D6 — what the nightly Branschbevakning scan last answered for a criterion. Stored by name
/// (reorder-safe) in <c>company_watch_criteria.last_scan_outcome</c>; <c>null</c> there means the criterion
/// has never been scanned. Only <see cref="Scanned"/> lets a count or a page of new ads be shown (ADR 0120:
/// a number is exact or absent).
/// </summary>
public enum CompanyWatchCriterionScanOutcome
{
    /// <summary>The member set was materialised for the current predicate; the window was read.</summary>
    Scanned,

    /// <summary>The criterion matches more companies than the breadth gate admits; no hit is written.</summary>
    TooBroad,

    /// <summary>
    /// No usable member set for the current predicate (never materialised, a stale fingerprint, or older than
    /// the read age). The scan holds its watermark, so the window is read once the set is materialised.
    /// </summary>
    NotMaterialised,
}
