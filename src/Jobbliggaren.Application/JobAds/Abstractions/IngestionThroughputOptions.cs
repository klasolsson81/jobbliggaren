using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Tunables for the ingestion-throughput fitness function (ADR 0045 Beslut 1
/// klass (d) — ≥ 200 jobb/min sustained). Application owns the contract; bound
/// in <c>AddJobSources</c> (Infrastructure/DependencyInjection.cs) — the
/// single module BOTH hosts pass through (Api via <c>AddInfrastructure</c>,
/// Worker directly), so a registration cannot structurally drift between them
/// (CTO bind #754 Q4; precedent <see cref="JobSourceRetentionOptions"/>).
/// </summary>
public sealed class IngestionThroughputOptions
{
    public const string SectionName = "IngestionThroughput";

    /// <summary>
    /// The throughput floor (ADR 0045 Beslut 1 klass (d), verbatim CTO-locked
    /// = 200 jobb/min). A qualifying run below this rate emits
    /// <c>IngestionThroughputBelowFloor</c> (Warning). Never change this
    /// default without a dated ADR 0045 amendment
    /// (<c>docs/runbooks/performance-measurement.md</c> §E — Goodhart's law,
    /// CLAUDE.md §2.5).
    /// </summary>
    [Range(1, 1_000_000)]
    public int FloorItemsPerMinute { get; set; } = 200;

    /// <summary>
    /// The sample-size gate a run must clear before ANY rate verdict is drawn
    /// (CTO bind #754 Q3). Deliberately reuses the floor's own numerator as
    /// its default — no new magic number: a "200/min" claim derived from
    /// fewer than 200 observed items is extrapolation, not measurement.
    /// Combined with <see cref="FloorItemsPerMinute"/>, this makes every
    /// possible warn a run that provably lasted &gt; 60s
    /// (<c>fetched &gt;= 200 ∧ fetched·60/durationSec &lt; 200 ⟹ durationSec &gt; 60s</c>)
    /// — "sustained" (ADR 0045 Beslut 1) is derived from these two numbers
    /// jointly, not a separately-configured duration knob (a
    /// <c>MinDurationSec</c> knob would be provably inert or harmful — see
    /// <see cref="Jobbliggaren.Application.JobAds.Jobs.Common.IngestionThroughputReporter"/>).
    /// </summary>
    [Range(1, 1_000_000)]
    public int MinItemsForVerdict { get; set; } = 200;
}
