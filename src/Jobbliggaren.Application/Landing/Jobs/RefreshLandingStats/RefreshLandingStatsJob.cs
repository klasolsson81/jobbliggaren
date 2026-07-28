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
///   <item>NewToday:    COUNT(*) WHERE PublishedAt &gt;= start of the SWEDISH day AND
///   Status='Active'. Not UTC (Klas-direktiv 2026-07-28, ADR 0064 Amendment
///   2026-07-28): the counter resets at Swedish midnight, because that is the
///   midnight the reader lives in. The boundary comes from
///   <see cref="ISwedishCalendar"/>, so DST is handled where the time zone lives
///   rather than here.</item>
/// </list>
/// Båda räknorna är indexerade (existerande <c>ix_job_ads_status</c> + partial
/// trigram-index för Active-rader); typisk latens på ~46k aktiva rader är sub-50ms.
/// </para>
/// </summary>
public sealed partial class RefreshLandingStatsJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    ISwedishCalendar calendar,
    ILandingStatsCache cache,
    ILogger<RefreshLandingStatsJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        // The instant Sweden's current day began — 23:00Z the previous day in
        // winter, 22:00Z in summer. Compared directly against the timestamptz
        // column, so no conversion happens inside the LINQ expression.
        var todayStart = calendar.StartOfDay(now);
        LogStarted(logger);

        var activeCount = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active)
            .CountAsync(cancellationToken).ConfigureAwait(false);

        var newToday = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active && j.PublishedAt >= todayStart)
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
