using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Admin.HostObservations;

/// <summary>
/// Takes one host sample per <see cref="Sample"/> call and keeps the newest as an immutable snapshot
/// (<see cref="IHostObservationReader"/>). It holds the one thing a CPU percentage needs across calls: the
/// previous counters with their monotonic timestamp.
///
/// <para>
/// <b>Lifetime: singleton.</b> Registered by <c>HostObservationRegistration</c> in the Api, called from
/// <c>HostObservationService</c>. Every dependency is singleton-safe, so it also survives
/// <c>ValidateScopes</c>. It is deliberately not a Mediator message: a synthetic one per tick would put
/// rows in the per-handler latency dataset (<c>docs/runbooks/performance-measurement.md</c> §A).
/// </para>
///
/// <para>
/// <b>Honesty.</b> A tick that cannot produce a reading writes <c>Failed</c> or <c>NotObservable</c> for it;
/// the previous value is not carried forward as if it were new, and nothing is ever defaulted to zero. A
/// CPU reading needs two good samples, so it is <c>Collecting</c> until the second. The wall clock stamps
/// <c>SampledAt</c> only; the CPU window is the probe's monotonic timestamps, which a step of the wall clock
/// cannot bend. Definitions: <c>docs/runbooks/admin-host-observations.md</c>.
/// </para>
/// </summary>
public sealed partial class HostObservationSampler(
    IHostObservationProbe probe,
    IDateTimeProvider clock,
    IOptions<HostObservationOptions> options,
    ILogger<HostObservationSampler> logger) : IHostObservationReader, IHostObservationSampler
{
    private readonly object _gate = new();
    private HostObservationSnapshot _current = HostObservationSnapshot.NotSampled(clock.UtcNow);
    private CpuCounters? _baseline;

    public HostObservationSnapshot Current => Volatile.Read(ref _current);

    /// <summary>
    /// Reads the host once and publishes the result. Never throws: a telemetry component must not be able
    /// to fault the process it runs in (the same rule as the Worker's memory sampler).
    /// </summary>
    public void Sample()
    {
        lock (_gate)
        {
            var previous = _current;
            HostObservationSnapshot next;
            try
            {
                next = Take(previous);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The probe contract says it does not throw; this is the last line should it do so anyway.
                _baseline = null;
                LogTickFailed(logger, ex);
                next = new HostObservationSnapshot(
                    clock.UtcNow,
                    HostMetric.Without<CpuValue>(HostMetricReason.ReadFailed),
                    HostMetric.Without<MemoryValue>(HostMetricReason.ReadFailed),
                    HostMetric.Without<DiskValue>(HostMetricReason.ReadFailed));
            }

            Volatile.Write(ref _current, next);
            LogEdges(previous, next);
        }
    }

    private HostObservationSnapshot Take(HostObservationSnapshot previous)
    {
        var cpu = probe.ReadCpu();
        var memory = probe.ReadMemory();
        var disk = probe.ReadDisk();
        var view = probe.ReadHostView();

        // Stamped once, after the reads: the instant every reading of this tick is attributed to.
        var at = clock.UtcNow;

        // A view that could not be read cannot disprove host scope, so sampling proceeds (documented residual).
        var containerScoped = view is { IsOk: true, Value: HostView.ContainerScoped };

        return new HostObservationSnapshot(
            at,
            Cpu(cpu, containerScoped, at, previous.Cpu),
            Memory(memory, containerScoped, at),
            Disk(disk, at));
    }

    private HostMetric<CpuValue> Cpu(
        ProbeResult<CpuCounters> read, bool containerScoped, DateTimeOffset at, HostMetric<CpuValue> previousMetric)
    {
        if (containerScoped)
        {
            _baseline = null;
            return HostMetric.Without<CpuValue>(HostMetricReason.HostViewUnverified);
        }

        if (!read.IsOk)
        {
            _baseline = null;
            return HostMetric.Without<CpuValue>(read.Failure!.Value);
        }

        var current = read.Value;
        if (_baseline is not { } baseline)
        {
            _baseline = current;
            return HostMetric.Collecting<CpuValue>();
        }

        var interval = TimeSpan.FromSeconds(options.Value.SampleIntervalSeconds);
        var window = current.At - baseline.At;

        // Under half an interval is noise, not a failure: keep the baseline and the reading it produced, with
        // its own sample time, so the next window is the longer one.
        if (window < interval / 2)
        {
            return previousMetric;
        }

        // Whatever the verdict below, this sample is the baseline for the next one: after a reset or a gap
        // the very next window is the first measurable one.
        _baseline = current;

        // Over three intervals the sampler did not run: an average over that gap would misstate "now".
        if (window > interval * 3)
        {
            return HostMetric.Without<CpuValue>(HostMetricReason.IntervalInvalid);
        }

        // Compared as grouped sums, not per column: the kernel does not promise that iowait never decreases.
        var busy = current.Busy - baseline.Busy;
        var idle = current.Idle - baseline.Idle;
        if (busy < 0 || idle < 0)
        {
            return HostMetric.Without<CpuValue>(HostMetricReason.CounterReset);
        }

        var total = busy + idle;
        if (total == 0)
        {
            return HostMetric.Without<CpuValue>(HostMetricReason.IntervalInvalid);
        }

        return HostMetric.Available(
            at,
            new CpuValue(Percent(busy, total), (int)Math.Round(window.TotalSeconds)));
    }

    private static HostMetric<MemoryValue> Memory(ProbeResult<MemoryReading> read, bool containerScoped, DateTimeOffset at)
    {
        if (containerScoped)
        {
            return HostMetric.Without<MemoryValue>(HostMetricReason.HostViewUnverified);
        }

        if (!read.IsOk)
        {
            return HostMetric.Without<MemoryValue>(read.Failure!.Value);
        }

        var (total, available) = (read.Value.TotalBytes, read.Value.AvailableBytes);
        if (total <= 0 || available < 0 || available > total)
        {
            return HostMetric.Without<MemoryValue>(HostMetricReason.Implausible);
        }

        var used = total - available;
        return HostMetric.Available(at, new MemoryValue(Percent(used, total), used, total));
    }

    private static HostMetric<DiskValue> Disk(ProbeResult<DiskReading> read, DateTimeOffset at)
    {
        if (!read.IsOk)
        {
            return HostMetric.Without<DiskValue>(read.Failure!.Value);
        }

        var (size, free, totalFree) = (read.Value.TotalBytes, read.Value.FreeBytes, read.Value.TotalFreeBytes);
        if (size <= 0 || free < 0 || totalFree < free || totalFree > size)
        {
            return HostMetric.Without<DiskValue>(HostMetricReason.Implausible);
        }

        // df's arithmetic: Use% = used / (used + avail). Blocks reserved for root are in neither, so the total
        // the card shows is used + free and not the filesystem's Size.
        var used = size - totalFree;
        var usable = used + free;
        return usable <= 0
            ? HostMetric.Without<DiskValue>(HostMetricReason.Implausible)
            : HostMetric.Available(at, new DiskValue(Percent(used, usable), free, usable));
    }

    private static double Percent(long part, long whole) =>
        Math.Round(100.0 * part / whole, 1, MidpointRounding.AwayFromZero);

    // Logged on the edge of a state, never per tick: a host that stays unreadable is one line, not 2 880 a day.
    private void LogEdges(HostObservationSnapshot previous, HostObservationSnapshot next)
    {
        LogEdge("cpu", previous.Cpu, next.Cpu);
        LogEdge("memory", previous.Memory, next.Memory);
        LogEdge("disk", previous.Disk, next.Disk);
    }

    private void LogEdge<T>(string metric, HostMetric<T> previous, HostMetric<T> next)
        where T : class
    {
        if (next.State == HostMetricState.Failed && previous.State != HostMetricState.Failed)
        {
            LogMetricFailed(logger, metric, next.Reason!.Value);
        }
        else if (next.Reason == HostMetricReason.HostViewUnverified && previous.Reason != HostMetricReason.HostViewUnverified)
        {
            LogHostViewUnverified(logger, metric);
        }
        else if (previous.State == HostMetricState.Failed && next.State == HostMetricState.Available)
        {
            LogMetricRecovered(logger, metric);
        }
    }

    [LoggerMessage(EventId = 6220, Level = LogLevel.Warning,
        Message = "HostObservation: {Metric} reading failed ({Reason}); it is reported as failed until a sample succeeds.")]
    private static partial void LogMetricFailed(ILogger logger, string metric, HostMetricReason reason);

    [LoggerMessage(EventId = 6221, Level = LogLevel.Information,
        Message = "HostObservation: {Metric} reading is available again.")]
    private static partial void LogMetricRecovered(ILogger logger, string metric);

    [LoggerMessage(EventId = 6222, Level = LogLevel.Warning,
        Message = "HostObservation: /proc looks container-scoped, so the {Metric} reading would not describe the host; it is withheld.")]
    private static partial void LogHostViewUnverified(ILogger logger, string metric);

    [LoggerMessage(EventId = 6223, Level = LogLevel.Warning,
        Message = "HostObservation: the probe threw; every reading of this tick is reported as failed.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
