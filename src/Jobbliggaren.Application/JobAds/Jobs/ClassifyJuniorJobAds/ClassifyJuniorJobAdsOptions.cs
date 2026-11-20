using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;

/// <summary>#2264 (ADR 0187) — the junior classification sweep's cadence and bounds. Every value has a default.</summary>
public sealed class ClassifyJuniorJobAdsOptions
{
    public const string SectionName = "ClassifyJuniorJobAds";

    /// <summary>The kill-switch. Off, the sweep does nothing and the list keeps the verdicts already stored.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Every two minutes: a new ad from the ten-minute stream reaches the list within one tick.</summary>
    [Required]
    public string Cron { get; set; } = "*/2 * * * *";

    /// <summary>Ads selected per round; rounds repeat until nothing is stale or the time budget is spent.</summary>
    [Range(1, 2000)]
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// How long one tick may keep starting rounds. Below the cadence, so a tick never runs into the next. A first
    /// deploy's backlog (every listed ad in the field) drains over a few ticks.
    /// </summary>
    [Range(10, 110)]
    public int TimeBudgetSeconds { get; set; } = 90;
}
