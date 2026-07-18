using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.CompanyWatches.Jobs.BackfillCompanyWatchOrgNrToken;

/// <summary>
/// Tunables for the one-off #544 backfill that tokenises existing PLAINTEXT personnummer-shaped
/// <c>company_watches.organization_number</c> values (ADR 0090 D5). Mirrors
/// <c>BackfillRecruiterContactScrubOptions</c>: a LOCAL pass (no external call), so the default
/// throttle is zero and the cap exists only as an operator brake. The company-watch set is tiny.
/// </summary>
public sealed class BackfillCompanyWatchOrgNrTokenOptions
{
    public const string SectionName = "BackfillCompanyWatchOrgNrToken";

    /// <summary>Optional per-item delay. Local re-projection → default 0.</summary>
    [Range(0, 10_000)]
    public int PerItemDelayMs { get; init; }

    /// <summary>Upper bound per run; a re-enqueue continues (the tokenisation is idempotent).</summary>
    [Range(1, 1_000_000)]
    public int MaxItemsPerRun { get; init; } = 200_000;

    [Range(1, 100_000)]
    public int ProgressLogEvery { get; init; } = 1_000;
}
