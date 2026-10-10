using Jobbliggaren.Application.Admin.HostObservations;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.UnitTests.Admin.HostObservations;

/// <summary>A wall clock the test moves by hand.</summary>
internal sealed class MutableClock(DateTimeOffset start) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// A probe that returns what the test sets, tick by tick. The defaults describe a healthy host of the shape
/// measured on the production box on 2026-10-10 (a 4 vCPU KVM guest with 8 GB RAM and one 250 GB root
/// filesystem), so a test states only what it changes. It stands in for
/// <c>ProcFsHostObservationProbe</c>; that class is tested against real files in its own tests.
/// </summary>
internal sealed class ScriptedHostProbe : IHostObservationProbe
{
    public ProbeResult<CpuCounters> Cpu { get; set; } =
        ProbeResult.Ok(new CpuCounters(1_000, 9_000, TimeSpan.FromSeconds(4_000)));

    public ProbeResult<MemoryReading> Memory { get; set; } =
        ProbeResult.Ok(new MemoryReading(8_135_992L * 1024, 5_743_556L * 1024));

    public ProbeResult<DiskReading> Disk { get; set; } =
        ProbeResult.Ok(new DiskReading(269_205_880_832, 242_287_181_824, 253_339_029_504));

    public ProbeResult<HostView> View { get; set; } = ProbeResult.Ok(HostView.HostScoped);

    public Exception? ThrowOnNextRead { get; set; }

    /// <summary>Runs inside every read, to prove when the sampler stamps its clock relative to the reads.</summary>
    public Action? OnRead { get; set; }

    public ProbeResult<CpuCounters> ReadCpu() => Read(Cpu);

    public ProbeResult<MemoryReading> ReadMemory() => Read(Memory);

    public ProbeResult<DiskReading> ReadDisk() => Read(Disk);

    public ProbeResult<HostView> ReadHostView() => Read(View);

    private T Read<T>(T value)
    {
        OnRead?.Invoke();
        if (ThrowOnNextRead is { } ex)
        {
            ThrowOnNextRead = null;
            throw ex;
        }

        return value;
    }

    /// <summary>
    /// The counters <paramref name="elapsed"/> later: <paramref name="busy"/> and <paramref name="idle"/> ticks
    /// further on. On this box (4 CPUs at 100 ticks a second) 30 s is 12 000 ticks in all.
    /// </summary>
    public static ProbeResult<CpuCounters> After(CpuCounters from, long busy, long idle, TimeSpan elapsed) =>
        ProbeResult.Ok(new CpuCounters(from.Busy + busy, from.Idle + idle, from.At + elapsed));
}
