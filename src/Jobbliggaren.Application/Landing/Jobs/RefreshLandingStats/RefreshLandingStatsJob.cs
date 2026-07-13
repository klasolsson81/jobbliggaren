using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Landing.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Landing.Jobs.RefreshLandingStats;

/// <summary>
/// Hangfire RecurringJob — beräknar landing-stats-aggregat och skriver till
/// <see cref="ILandingStatsCache"/>. ADR 0064 Variant B (pre-computed Redis-cache).
///
/// <para>
/// Cron <c>*/5 * * * *</c> UTC. Delar "DB-bunden lane" med övriga retention-/
/// stats-jobb; den 6 ggr/timme krock med stream-cron (<c>*/10</c>) är acceptabel
/// eftersom stream-cron är HTTP-bunden mot JobTech, inte DB-bunden (paritet
/// <see cref="Jobbliggaren.Worker.Hosting.RecurringJobRegistrar"/>-docs).
/// </para>
/// <para>
/// Idempotent: överskriver hela cache-nyckeln per körning. Concurrent
/// execution förhindrad via Worker-wrapper <c>RefreshLandingStatsWorker</c>
/// (<see cref="Hangfire.DisableConcurrentExecutionAttribute"/>).
/// </para>
/// <para>
/// Två räknor (ADR 0056 spec, Klas-bekräftat 2026-05-23):
/// <list type="bullet">
///   <item>ActiveCount: COUNT(*) WHERE Status='Active'. Status ÄR hela avgränsningen — JobAd har
///   ingen soft-delete-axel och inget query-filter (#821).</item>
///   <item>NewToday:    COUNT(*) WHERE PublishedAt &gt;= today UTC AND Status='Active'.</item>
/// </list>
/// Båda räknorna är indexerade (existerande <c>ix_job_ads_status</c> + partial
/// trigram-index för Active-rader); typisk latens på ~46k aktiva rader är sub-50ms.
/// </para>
/// </summary>
public sealed partial class RefreshLandingStatsJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    ILandingStatsCache cache,
    ILogger<RefreshLandingStatsJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var todayUtcStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        LogStarted(logger);

        var activeCount = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        var newToday = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active && j.PublishedAt >= todayUtcStart)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        var stats = new LandingStatsDto(activeCount, newToday, IsStale: false, RefreshedAt: now);
        await cache.SetAsync(stats, cancellationToken).ConfigureAwait(false);

        LogCompleted(logger, activeCount, newToday);
    }

    [LoggerMessage(EventId = 5901, Level = LogLevel.Information,
        Message = "RefreshLandingStatsJob: startad.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 5902, Level = LogLevel.Information,
        Message = "RefreshLandingStatsJob: klart — activeCount={ActiveCount}, newToday={NewToday}.")]
    private static partial void LogCompleted(ILogger logger, int activeCount, int newToday);
}
