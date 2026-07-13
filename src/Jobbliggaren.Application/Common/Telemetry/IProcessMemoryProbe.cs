namespace Jobbliggaren.Application.Common.Telemetry;

/// <summary>
/// Port over process-level memory telemetry (ADR 0045 Beslut 3 — Worker 512 MiB
/// working-set soft cap). <c>Environment.WorkingSet</c> and <c>GC</c> are ambient
/// platform statics — the same class of untestable, un-injected dependency
/// CLAUDE.md §5 already forbids for <c>DateTime.UtcNow</c> (see
/// <see cref="Jobbliggaren.Domain.Common.IDateTimeProvider"/>). Application
/// defines the port; Infrastructure (<c>ProcessMemoryProbe</c>) implements it
/// against the real platform APIs (CTO bind #754 Q2).
/// </summary>
public interface IProcessMemoryProbe
{
    /// <summary>Takes one point-in-time sample of process memory.</summary>
    ProcessMemorySample Sample();
}

/// <summary>
/// One point-in-time process memory sample (ADR 0045 Beslut 3).
///
/// <para>
/// <b>Process-scoped, not per-job.</b> <see cref="WorkingSetBytes"/> is the
/// WHOLE Worker process's working set. With <c>WorkerCount = 4</c>, up to four
/// Hangfire jobs share the process at once, so there is no honest in-process
/// attribution of a byte count to a single job instance — see the dated ADR
/// 0045 Beslut 3 amendment and <c>docs/runbooks/performance-measurement.md</c>
/// §B for the full reasoning and how to correlate a sample to a specific job
/// run instead (by time window, at read time, in Seq).
/// </para>
/// </summary>
/// <param name="WorkingSetBytes">Physical working-set (<c>Environment.WorkingSet</c>).</param>
/// <param name="GcHeapBytes">Managed heap bytes (<c>GC.GetTotalMemory(forceFullCollection: false)</c> — no forced collection, so the sampler does not itself perturb the GC it observes).</param>
/// <param name="Gen2Collections">Cumulative gen2 collection count (<c>GC.CollectionCount(2)</c>) — a rising count alongside a rising working set is the ADR 0032 memory-pressure signature, distinct from a large-but-flat steady-state cache.</param>
public readonly record struct ProcessMemorySample(long WorkingSetBytes, long GcHeapBytes, int Gen2Collections);
