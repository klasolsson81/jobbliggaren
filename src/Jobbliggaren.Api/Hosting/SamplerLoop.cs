namespace Jobbliggaren.Api.Hosting;

/// <summary>
/// The control flow every sampler hosted in the API runs on (ADR 0159 D2, ADR 0174): one tick at once, then one per
/// interval. A <c>PeriodicTimer</c>, not a Hangfire job: a sampler must not wait for a worker slot.
///
/// <para>
/// <b>It must never be able to stop the host.</b> <c>BackgroundServiceExceptionBehavior</c> defaults to
/// <c>StopHost</c>, so an exception escaping <c>ExecuteAsync</c> would stop the whole API. Each service wraps its tick
/// in <see cref="Guard"/> or <see cref="GuardAsync"/>: only the shutdown token's cancellation leaves the loop, and
/// the failure report is itself guarded, because a failing log sink is the likeliest thing to throw there. Each
/// service keeps its own log line, so the report is a callback. The Worker's memory sampler keeps a guard of its own:
/// the Worker cannot reference the Api.
/// </para>
/// </summary>
internal static class SamplerLoop
{
    public static async Task RunAsync(
        TimeSpan interval, Func<CancellationToken, Task> guardedTick, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            await guardedTick(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await guardedTick(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown: the stopping token was cancelled. Expected, not a fault.
        }
    }

    public static void Guard(Action sample, Action<Exception> reportFailure, CancellationToken stoppingToken)
    {
        try
        {
            sample();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown only. Without the filter a stray cancellation from inside the tick would be read as a
            // graceful shutdown by the outer catch and the loop would end silently for the rest of the process.
            throw;
        }
        catch (Exception ex)
        {
            Report(reportFailure, ex);
        }
    }

    public static async Task GuardAsync(
        Func<CancellationToken, Task> sample, Action<Exception> reportFailure, CancellationToken stoppingToken)
    {
        try
        {
            await sample(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown only, as in Guard.
            throw;
        }
        catch (Exception ex)
        {
            Report(reportFailure, ex);
        }
    }

    private static void Report(Action<Exception> reportFailure, Exception exception)
    {
        // Nested: the realistic way to reach it is a failing log sink, which would throw here as well.
        try
        {
            reportFailure(exception);
        }
        catch (Exception)
        {
            // Terminal by design. The next tick may well succeed.
        }
    }
}
