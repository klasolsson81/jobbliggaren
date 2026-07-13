using Jobbliggaren.Application.Common.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Periodic Worker memory trend sampler (ADR 0045 Beslut 3 — 512 MiB
/// working-set soft cap, Fas 1 "trend-logg + alarm-tröskel-förberedelse").
///
/// <para>
/// <b>Why <see cref="BackgroundService"/> + <see cref="PeriodicTimer"/>, not a
/// Hangfire recurring job</b> (CTO bind #754 Q1): a Hangfire job competes for
/// <c>WorkerCount = 4</c> job slots with the snapshot/stream/matching/digest/
/// etc. jobs. Under a saturated pool — precisely when memory pressure is
/// highest, e.g. during the tens-of-minutes, ~47k-item snapshot — a
/// Hangfire-scheduled sampler simply does not run. An instrument that is
/// blind exactly when the thing it observes is under load is not a degraded
/// instrument, it is a broken one. This sampler runs on a wall-clock timer,
/// independent of Hangfire's queue state — no job-history pollution, no
/// per-tick DB round-trip.
/// </para>
///
/// <para>
/// <b>Stability (non-negotiable, CTO bind #754 Q1).</b> The default
/// <c>HostOptions.BackgroundServiceExceptionBehavior</c> is <c>StopHost</c> —
/// an unhandled exception escaping <c>ExecuteAsync</c> stops the ENTIRE
/// Worker host, killing every Hangfire job with it (hard-delete-accounts,
/// digest-dispatch, all of it). A telemetry component must never be able to
/// fault the process it monitors (Nygard, <i>Release It!</i> 2nd ed. 2018).
/// Two independent layers guard this:
/// <list type="bullet">
///   <item><see cref="WorkerMemoryTrendSampler.Sample"/> already catches a
///     probe/sink failure internally and never throws;</item>
///   <item>this class ALSO wraps each tick, as defense-in-depth against
///     anything else that could escape — a probe/sampler failure here is
///     logged at Warning and the loop continues. Only
///     <see cref="OperationCanceledException"/> from the shutdown token exits
///     the loop — the standard graceful-shutdown shape for a
///     <see cref="PeriodicTimer"/>-driven <see cref="BackgroundService"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Lifetime.</b> Registered via <c>AddHostedService&lt;T&gt;()</c>
/// (framework-singleton). <see cref="WorkerMemoryTrendSampler"/> is itself
/// registered singleton (it holds the below/above-cap edge state across
/// ticks) — the Worker runs <c>ValidateScopes = true</c>, so every dependency
/// in this chain must be singleton-safe. It is, by construction: no
/// <c>IServiceScopeFactory</c>, no scoped dependency anywhere in the chain.
/// </para>
/// </summary>
public sealed partial class WorkerMemoryTrendService(
    WorkerMemoryTrendSampler sampler,
    IOptions<WorkerMemoryTrendOptions> options,
    ILogger<WorkerMemoryTrendService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SampleIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    sampler.Sample();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Defense-in-depth (CTO bind #754 Q1) — Sample() already
                    // guards internally; this catch is last-resort so this
                    // hosted service can NEVER stop the Worker host regardless
                    // of what future change might land inside Sample().
                    LogTickFailed(logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — stoppingToken cancelled. Expected, not a fault.
        }
    }

    [LoggerMessage(EventId = 6214, Level = LogLevel.Warning,
        Message = "WorkerMemoryTrendService: unexpected tick failure — sample skipped, host continues.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
