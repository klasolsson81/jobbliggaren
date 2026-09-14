using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1682 — the occupation × SNI-division profile job's own options section. Its OWN section, and
/// that is the point (security-auditor Major 3 on ADR 0139, applied here by senior-cto-advisor D1,
/// 2026-09-14): a cadence tied to another feature's <c>Enabled</c> flag runs exactly as often as that
/// flag is true, and <c>ScbRegister:Enabled</c> defaults false. This job reads nothing from
/// <c>ScbRegister:*</c> or <c>CompanyWatchMaterialisation:*</c>.
///
/// <para>
/// <see cref="CadenceCron"/> is clock-padded after the daily snapshot ingest, not chained to it:
/// the snapshot's window is 02:00 UTC plus a 3 600 s concurrency lock, so it is clear by 03:00, and
/// 03:35 sits inside the existing ingest-consumer cluster (retain 03:15, matching 03:20, watch-scan
/// 03:25, expire 03:45) with ten minutes' padding either side. A level-triggered watermark on the
/// snapshot's audit row was measured out: <c>job_ads</c> has TWO writers, the stream job on
/// <c>*/10</c> being the other, so a "once per completed snapshot" trigger would fire exactly as
/// often as this cron while claiming to track ingest (senior-cto-advisor D1). ⚠ If the snapshot
/// cadence moves or its runtime grows past this pad, this cron is re-derived — not nudged.
/// </para>
/// </summary>
public sealed class OccupationDivisionProfileOptions
{
    public const string SectionName = "OccupationDivisionProfile";

    /// <summary>
    /// Kill-switch, default ON. The job guards a purely local recompute over tables we already hold;
    /// defaulting it off would make an independent trigger independently switched off (the
    /// <c>CompanyWatchMaterialisationOptions.Enabled</c> argument). Off, the read side degrades to
    /// "not profiled" once the last run passes <see cref="MaxReadAgeHours"/> — never to a zero.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Hangfire cron, UTC. <c>[Required]</c> rejects null/empty only; a syntactically broken cron is
    /// rejected by Hangfire's <c>AddOrUpdate</c> at Worker boot, not here.
    /// </summary>
    [Required]
    public string CadenceCron { get; set; } = "35 3 * * *";

    /// <summary>
    /// The read side refuses to render a profile older than this. DERIVED as two cadence intervals
    /// on the daily cron: one missed run is transient, two consecutive is a broken job
    /// (senior-cto-advisor D1). Named as one decision with <see cref="CadenceCron"/> — change the
    /// cadence and this moves with it.
    /// </summary>
    [Range(1, 8_760)]
    public int MaxReadAgeHours { get; set; } = 48;

    /// <summary>
    /// A huvudgrupp is shown when it holds at least this share of the occupation group's ads.
    /// Issue #1682 scope 2. Applied at read time, never at write time.
    /// </summary>
    [Range(1, 100)]
    public int MinimumSharePercent { get; set; } = 5;

    /// <summary>
    /// DERIVED from <see cref="MinimumSharePercent"/>, never configured beside it: it is the smallest
    /// N at which a single ad cannot clear the share threshold alone (100/N &lt; p). Below it the
    /// "≥ p %" list is an enumeration of individual employers wearing the grammar of a distribution.
    /// At 5 % that is 21 (1/20 = 5.0 % clears, 1/21 = 4.76 % does not). Measured on the box
    /// 2026-09-14: 310 of 395 groups clear it, 85 are refused
    /// (docs/reviews/2026-09-14-1682-profile-measurement.md §3). Two settings that must agree drift
    /// apart at the first edit, so there is one.
    /// </summary>
    public int MinimumAdsPerGroup => (100 / MinimumSharePercent) + 1;
}
