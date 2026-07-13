using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Common.Telemetry;

/// <summary>
/// Tunables for the Worker memory trend sampler (ADR 0045 Beslut 3 — 512 MiB
/// working-set soft cap, Fas 1 "trend-logg + alarm-tröskel-förberedelse").
/// Application owns the contract; Worker binds it (<c>WorkerMemoryTrend</c>
/// section) with <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> in
/// <c>Worker/Program.cs</c> ONLY — this instrument exists only there (CTO bind
/// #754 Q4; a shared module would push a Worker-only concern into the Api's
/// container for nothing).
/// </summary>
public sealed class WorkerMemoryTrendOptions
{
    public const string SectionName = "WorkerMemoryTrend";

    /// <summary>
    /// Sampling interval in seconds. Default 60 — a tens-of-minutes snapshot
    /// run yields ~30-60 samples, enough to see a memory ramp; sub-60s buys no
    /// extra resolution on a job that long and multiplies log volume (CTO bind
    /// #754 Q4). A seconds-long stream run is not resolved by this interval,
    /// and does not need to be — the stream processes a bounded 15-minute
    /// delta; the snapshot is the OOM risk this instrument backstops.
    /// </summary>
    [Range(1, 3600)]
    public int SampleIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Soft cap in MiB (ADR 0045 Beslut 3, verbatim CTO-locked = 512).
    /// Observe-only Fas 1 — a breach warns, it never blocks CI. Never change
    /// this default without a dated ADR 0045 amendment
    /// (<c>docs/runbooks/performance-measurement.md</c> §E — a cap change is
    /// never a silent config bump, Goodhart's law / CLAUDE.md §2.5).
    /// </summary>
    [Range(1, 65536)]
    public int SoftCapMiB { get; set; } = 512;
}
