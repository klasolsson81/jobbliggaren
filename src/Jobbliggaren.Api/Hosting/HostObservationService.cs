using Jobbliggaren.Application.Admin.HostObservations;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Api.Hosting;

/// <summary>
/// Calls the host sampler on a timer for the lifetime of the API (#1982): once at once, so memory and disk
/// are known within the first second of a release, then every <c>SampleIntervalSeconds</c>. A
/// <c>PeriodicTimer</c> on the wall-clock, not a Hangfire job: the sampler must not wait for a worker slot.
///
/// <para>
/// <b>It must never be able to stop the host.</b> <c>BackgroundServiceExceptionBehavior</c> defaults to
/// <c>StopHost</c>, so an exception escaping <c>ExecuteAsync</c> would stop the whole API. Every tick,
/// the first included, runs inside the same nested guard as the Worker's memory sampler
/// (<c>WorkerMemoryTrendService</c>): only the shutdown token's cancellation leaves the loop, and the last
/// handler is itself guarded because a failing log sink is the likeliest thing to throw there.
/// </para>
/// </summary>
public sealed partial class HostObservationService(
    IHostObservationSampler sampler,
    IOptions<HostObservationOptions> options,
    ILogger<HostObservationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SampleIntervalSeconds));
        try
        {
            Tick(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Tick(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown: the stopping token was cancelled. Expected, not a fault.
        }
    }

    internal void Tick(CancellationToken stoppingToken)
    {
        try
        {
            sampler.Sample();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown only. Without the filter a stray cancellation from inside the tick would be read as a
            // graceful shutdown by the outer catch and the loop would end silently for the rest of the process.
            throw;
        }
        catch (Exception ex)
        {
            // The sampler contract says it does not throw; this is the last line should that ever change.
            // Nested: the realistic way to reach it is a failing log sink, which would throw here as well.
            try
            {
                LogTickFailed(logger, ex);
            }
            catch (Exception)
            {
                // Terminal by design. The next tick may well succeed.
            }
        }
    }

    [LoggerMessage(EventId = 6224, Level = LogLevel.Warning,
        Message = "HostObservationService: unexpected tick failure — sample skipped, host continues.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
