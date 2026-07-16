using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.JobAds.Jobs.BackfillRecruiterContactScrub;

/// <summary>
/// Tunables for the one-off Tier-A contact-scrub backfill (#842, re-bind R7/D10). Mirrors
/// <c>BackfillJobAdExtractedTermsOptions</c>: a LOCAL pass (no JobTech fetch), so the default
/// throttle is zero and the cap exists only as an operator brake.
/// </summary>
public sealed class BackfillRecruiterContactScrubOptions
{
    public const string SectionName = "BackfillRecruiterContactScrub";

    /// <summary>Optional per-item delay. Local re-projection → default 0.</summary>
    [Range(0, 10_000)]
    public int PerItemDelayMs { get; init; }

    /// <summary>Upper bound per run; a re-enqueue continues (the scrub is idempotent).</summary>
    [Range(1, 1_000_000)]
    public int MaxItemsPerRun { get; init; } = 200_000;

    [Range(1, 100_000)]
    public int ProgressLogEvery { get; init; } = 1_000;
}
