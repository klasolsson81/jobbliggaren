using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchCriterionScan;

/// <summary>
/// ADR 0175 D11 — the nightly Branschbevakning scan's cadence. Application owns the contract; the Worker binds the
/// <c>CompanyWatchCriterionScan</c> section with <c>ValidateDataAnnotations</c> and <c>ValidateOnStart</c>. Its own
/// section, never the materialisation's: a cadence tied to another feature's options runs as often as that
/// feature says (ADR 0139). The code default means a missing key boots, so it is no CLAUDE.md §11 required key.
/// </summary>
public sealed class CompanyWatchCriterionScanOptions
{
    public const string SectionName = "CompanyWatchCriterionScan";

    /// <summary>
    /// UTC. The default, 03:55, follows the follow scan's 03:25 slot and its 1 800-second lock wait and precedes
    /// the 05:30 materialisation. The slot is a latency choice only: the scan's upper bound is the owner's follow
    /// watermark whatever the order (ADR 0175 D3). <c>[Required]</c> rejects only an empty value; a malformed cron
    /// fails at Worker boot in <c>AddOrUpdate</c>.
    /// </summary>
    [Required]
    public string CadenceCron { get; set; } = "55 3 * * *";
}
