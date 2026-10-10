using Jobbliggaren.Application.Admin.ServiceObservations;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Api.Hosting;

/// <summary>
/// Calls the services sampler on a timer for the lifetime of the API (#1982, ADR 0159): once at once, then every
/// <c>SampleIntervalSeconds</c>. A <c>PeriodicTimer</c>, not a Hangfire job: the sampler must not wait for a
/// worker slot, and it reads the Worker's own heartbeat, so it cannot run in the Worker.
///
/// <para>
/// <b>It must never be able to stop the host.</b> <c>BackgroundServiceExceptionBehavior</c> defaults to
/// <c>StopHost</c>, so an exception escaping <c>ExecuteAsync</c> would stop the whole API. Every tick, the first
/// included, runs inside the same nested guard as <see cref="HostObservationService"/>: only the shutdown token's
/// cancellation leaves the loop, and the last handler is itself guarded because a failing log sink is the
/// likeliest thing to throw there. The two samplers are not merged into one loop: their ticks differ (one
/// synchronous, one asynchronous), and a third sampler is what would justify the shared form.
/// </para>
/// </summary>
public sealed partial class ServiceObservationService(
    IServiceObservationSampler sampler,
    IOptions<ServiceObservationOptions> options,
    ILogger<ServiceObservationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SampleIntervalSeconds));
        try
        {
            await TickAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await TickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown: the stopping token was cancelled. Expected, not a fault.
        }
    }

    internal async Task TickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await sampler.SampleAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown only. Without the filter a stray cancellation from inside the tick would be read as a
            // graceful shutdown by the outer catch and the loop would end silently for the rest of the process.
            throw;
        }
        catch (Exception ex)
        {
            // Nested: the realistic way to reach it is a failing log sink, which would throw here as well.
            try
            {
                LogTickFailed(logger, ex.GetType().Name);
            }
            catch (Exception)
            {
                // Terminal by design. The next tick may well succeed.
            }
        }
    }

    [LoggerMessage(EventId = 6232, Level = LogLevel.Warning,
        Message = "ServiceObservationService: unexpected tick failure ({ExceptionType}) — sample skipped, host continues.")]
    private static partial void LogTickFailed(ILogger logger, string exceptionType);
}
