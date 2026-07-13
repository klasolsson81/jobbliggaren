using Jobbliggaren.Application.Common.Telemetry;

namespace Jobbliggaren.Infrastructure.Diagnostics;

/// <summary>
/// Adapter for <see cref="IProcessMemoryProbe"/> (ADR 0045 Beslut 3). Reads the
/// real platform ambient statics exactly once per <see cref="Sample"/> call —
/// no forced GC collection, so the act of sampling does not itself perturb the
/// process it observes (CTO bind #754 Q2).
/// </summary>
public sealed class ProcessMemoryProbe : IProcessMemoryProbe
{
    public ProcessMemorySample Sample() => new(
        WorkingSetBytes: Environment.WorkingSet,
        GcHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
        Gen2Collections: GC.CollectionCount(2));
}
