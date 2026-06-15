using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms;

/// <summary>
/// Configuration for <see cref="BackfillJobAdExtractedTermsJob"/>. Bound to the
/// <c>BackfillJobAdExtractedTerms</c> section of <c>appsettings.json</c>. Unlike
/// the Klass2/SSYK backfill there is NO external (JobTech) throttle to tune — this
/// is a local re-projection — so <see cref="PerItemDelayMs"/> defaults to 0.
/// <see cref="MaxItemsPerRun"/> is a defense-in-depth cap; lower it for a test
/// batch.
/// </summary>
public sealed class BackfillJobAdExtractedTermsOptions
{
    public const string SectionName = "BackfillJobAdExtractedTerms";

    [Range(0, 60_000)]
    public int PerItemDelayMs { get; set; }

    [Range(1, 1_000_000)]
    public int MaxItemsPerRun { get; set; } = 1_000_000;

    [Range(1, 100_000)]
    public int ProgressLogEvery { get; set; } = 1000;
}
