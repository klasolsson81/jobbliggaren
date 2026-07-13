using Jobbliggaren.Application.Common.Telemetry;

namespace Jobbliggaren.Application.UnitTests.Common.Telemetry;

/// <summary>
/// Scripted fake <see cref="IProcessMemoryProbe"/> — returns one queued
/// <see cref="ProcessMemorySample"/> per <see cref="Sample"/> call, in order.
/// If <see cref="ThrowOnNextSample"/> is set, the NEXT call throws that
/// exception instead of dequeuing (used to verify
/// <c>WorkerMemoryTrendSampler</c>'s stability guard — CTO bind #754 Q1).
/// </summary>
internal sealed class ScriptedProcessMemoryProbe(params ProcessMemorySample[] samples) : IProcessMemoryProbe
{
    private readonly Queue<ProcessMemorySample> _samples = new(samples);

    public Exception? ThrowOnNextSample { get; set; }

    public ProcessMemorySample Sample()
    {
        if (ThrowOnNextSample is { } ex)
        {
            ThrowOnNextSample = null;
            throw ex;
        }

        return _samples.Dequeue();
    }

    /// <summary>Builds a sample at the given MiB working-set (GC heap mirrors it; gen2 defaults to 0).</summary>
    public static ProcessMemorySample AtMiB(double mib, int gen2Collections = 0)
    {
        var bytes = (long)(mib * 1024 * 1024);
        return new ProcessMemorySample(WorkingSetBytes: bytes, GcHeapBytes: bytes, Gen2Collections: gen2Collections);
    }
}
