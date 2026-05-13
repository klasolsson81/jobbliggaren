using Hangfire;
using JobbPilot.Application.Applications.Jobs.GhostedDetection;
using JobbPilot.Application.Auth.Jobs.HardDeleteAccounts;
using JobbPilot.Application.Common.Auditing.Jobs.AuditLogRetention;
using JobbPilot.Application.JobAds.Jobs.PurgeRawPayloads;
using JobbPilot.Application.JobAds.Jobs.SyncPlatsbanken;
using Microsoft.Extensions.Hosting;

namespace JobbPilot.Worker.Hosting;

/// <summary>
/// Registrerar Hangfire <see cref="RecurringJob"/>:s vid Worker-host start.
/// Idempotent — <see cref="IRecurringJobManager.AddOrUpdate{T}(string, System.Linq.Expressions.Expression{System.Action{T}}, string, RecurringJobOptions)"/>
/// kan köras flera gånger utan biverkningar.
///
/// Cron-tider är UTC (Hangfire-default). Schedule (CTO-rond 2026-05-13 punkt 8):
///   */10 *  — sync-platsbanken-stream (10-min cron, overlap-window 15 min)
///   02:00   — sync-platsbanken-snapshot (daglig fullbackfill mot stream-drift)
///   03:00   — audit-log-retention (atomisk partition-DDL, &lt; 100ms typiskt)
///   03:30   — detect-ghosted (DML på applications + audit-skrivningar)
///   04:00   — hard-delete-accounts (1h efter retention)
///   04:30   — purge-stale-raw-payloads (30-min padding efter hard-delete)
///
/// 30-min-padding mellan jobben eliminerar kollision på Hangfire-dashboard
/// vid pålastnings-toppar — även om jobben rör olika tabeller är padding
/// gratis försäkring + tydliga recovery-fönster.
///
/// JobTech-snapshot förlagd till 02:00 UTC (separat timme från admin-jobben)
/// eftersom snapshot kan ta minutar (50-100 MB JSON-parse + tusentals upserts).
/// Stream-cron `*/10` kolliderar 6 ggr/timme med övriga slottar — acceptabelt
/// eftersom stream-cron är HTTP-bunden mot JobTech, inte DB-bunden.
///
/// 02:00 UTC motsvarar svensk natt (03:00 vintertid / 04:00 sommartid) —
/// lägst belastning på dev-DB och ingen konflikt med interaktiv användning.
/// </summary>
public sealed class RecurringJobRegistrar(IRecurringJobManager manager) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        manager.AddOrUpdate<SyncPlatsbankenStreamWorker>(
            "sync-platsbanken-stream",
            job => job.RunAsync(CancellationToken.None),
            "*/10 * * * *");  // Var 10:e min, overlap-window 15 min, DisableConcurrentExecution-skyddad

        manager.AddOrUpdate<SyncPlatsbankenSnapshotJob>(
            "sync-platsbanken-snapshot",
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(2));  // 02:00 UTC — daglig fullbackfill

        manager.AddOrUpdate<AuditLogRetentionJob>(
            "audit-log-retention",
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(3));

        manager.AddOrUpdate<DetectGhostedApplicationsJob>(
            "detect-ghosted",
            job => job.RunAsync(CancellationToken.None),
            "30 3 * * *");  // 03:30 UTC — 30-min padding efter audit-log-retention

        manager.AddOrUpdate<HardDeleteAccountsJob>(
            "hard-delete-accounts",
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(4));

        manager.AddOrUpdate<PurgeStaleRawPayloadsJob>(
            "purge-stale-raw-payloads",
            job => job.RunAsync(CancellationToken.None),
            "30 4 * * *");  // 04:30 UTC — 30-min padding efter hard-delete (TD-73 punkt 2)

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
