using Hangfire;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.BackgroundJobs;
using Jobbliggaren.Application.Common.Auditing.Jobs.AuditLogRetention;
using Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds;
using Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads;
using Jobbliggaren.Application.JobAds.Jobs.RetainPlatsbankenJobAds;
using Jobbliggaren.Application.Landing.Jobs.RefreshLandingStats;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Registrerar Hangfire <see cref="RecurringJob"/>:s vid Worker-host start.
/// Idempotent — <see cref="IRecurringJobManager.AddOrUpdate{T}(string, System.Linq.Expressions.Expression{System.Action{T}}, string, RecurringJobOptions)"/>
/// kan köras flera gånger utan biverkningar.
///
/// Cron-tider är UTC (Hangfire-default). Schedule (CTO-rond 2026-05-13 punkt 8
/// + architect-design 2026-05-23 retention):
///   */10 *  — sync-platsbanken-stream (10-min cron, overlap-window 15 min)
///   02:00   — sync-platsbanken-snapshot (daglig fullbackfill mot stream-drift)
///   03:00   — audit-log-retention (atomisk partition-DDL, &lt; 100ms typiskt)
///   03:15   — retain-platsbanken-job-ads (snapshot-miss-retention, ADR 0032-amend 2026-05-23)
///   03:20   — background-matching (per-user matchnings-scan: läser JobAds → skriver UserJobAdMatch, ADR 0080 Vag 4 PR-3)
///   03:25   — company-watch-scan (per-user följnings-scan: JobAds IN watched org.nr → FollowedCompanyAdHit, ADR 0087 D5)
///   03:45   — expire-job-ads (ExpiresAt-cron, defense-in-depth, ADR 0032-amend 2026-05-23)
///   04:00   — hard-delete-accounts (1h efter retention)
///   04:30   — purge-stale-raw-payloads (30-min padding efter hard-delete)
///   05:00   — backfill-field-encryption (30-min padding efter purge)
///   06:00   — digest-dispatch-daily (Strong-digest, daglig kadens, ADR 0080 Vag 4 PR-4b)
///   06:00 mån — digest-dispatch-weekly (Strong-digest, veckovis kadens — civic-default)
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
public sealed class RecurringJobRegistrar(
    IRecurringJobManager manager,
    IOptions<ScbRegisterOptions> scbOptions) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        manager.AddOrUpdate<SyncPlatsbankenStreamWorker>(
            RecurringJobIds.SyncPlatsbankenStream,
            job => job.RunAsync(CancellationToken.None),
            "*/10 * * * *");  // Var 10:e min, overlap-window 15 min, DisableConcurrentExecution-skyddad

        manager.AddOrUpdate<SyncPlatsbankenSnapshotWorker>(
            RecurringJobIds.SyncPlatsbankenSnapshot,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(2));  // 02:00 UTC — daglig fullbackfill, DisableConcurrentExecution(3600)-skyddad

        manager.AddOrUpdate<AuditLogRetentionJob>(
            RecurringJobIds.AuditLogRetention,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(3));

        manager.AddOrUpdate<RetainPlatsbankenJobAdsWorker>(
            RecurringJobIds.RetainPlatsbankenJobAds,
            job => job.RunAsync(CancellationToken.None),
            "15 3 * * *");  // 03:15 UTC — efter snapshot-fönstret (02:00, upp till 60 min) + audit-log-retention

        manager.AddOrUpdate<BackgroundMatchingWorker>(
            RecurringJobIds.BackgroundMatching,
            job => job.RunAsync(CancellationToken.None),
            "20 3 * * *");  // 03:20 UTC — efter retain (03:15), före company-watch-scan (03:25). Per-user matchnings-scan: läser JobAds (Active) → skriver UserJobAdMatch + JobSeeker.LastMatchScanAt (ortogonalt mot retain). ADR 0080 Vag 4 PR-3.

        manager.AddOrUpdate<CompanyWatchScanWorker>(
            RecurringJobIds.CompanyWatchScan,
            job => job.RunAsync(CancellationToken.None),
            "25 3 * * *");  // 03:25 UTC — samma nattfönster som background-matching (03:20), egen
                            // watermark. Företagsföljnings-scan: läser JobAds (Active) IN watched org.nr
                            // → skriver FollowedCompanyAdHit + JobSeeker.LastCompanyWatchScanAt (ortogonalt
                            // mot background-matching; ingen scorer). Före digest (06:00). ADR 0087 D5.

        manager.AddOrUpdate<ExpireJobAdsWorker>(
            RecurringJobIds.ExpireJobAds,
            job => job.RunAsync(CancellationToken.None),
            "45 3 * * *");  // 03:45 UTC — defense-in-depth ExpiresAt-cron (ADR 0032-amend 2026-05-23)

        manager.AddOrUpdate<HardDeleteAccountsJob>(
            RecurringJobIds.HardDeleteAccounts,
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(4));

        manager.AddOrUpdate<PurgeStaleRawPayloadsJob>(
            RecurringJobIds.PurgeStaleRawPayloads,
            job => job.RunAsync(CancellationToken.None),
            "30 4 * * *");  // 04:30 UTC — 30-min padding efter hard-delete (TD-73 punkt 2)

        manager.AddOrUpdate<StrandedMatchReaperWorker>(
            RecurringJobIds.ReapStrandedMatches,
            job => job.RunAsync(CancellationToken.None),
            "45 4 * * *");  // 04:45 UTC — TD-114. Reapar UserJobAdMatch som fastnat i Queued
                            // (failad send) → terminal Failed. EFTER förra cykelns digest-fönster
                            // (06:00 dagen innan) hunnit sätta sig, i hard-delete-klustrets lugna
                            // svans (mellan purge 04:30 och backfill 05:00). DisableConcurrentExecution-skyddad.

        manager.AddOrUpdate<BackfillFieldEncryptionWorker>(
            RecurringJobIds.BackfillFieldEncryption,
            job => job.RunAsync(CancellationToken.None),
            "0 5 * * *");  // 05:00 UTC — 30-min padding efter purge (TD-13 C5, ADR 0049 Beslut 4)

        manager.AddOrUpdate<ParsedResumeRetentionWorker>(
            RecurringJobIds.ParsedResumeRetention,
            job => job.RunAsync(CancellationToken.None),
            "15 5 * * *");  // 05:15 UTC — TD-111 (ADR 0074 F4-8). Set-based ExecuteDelete-svep av
                            // mognade ParsedResume-staging-rader (Discarded/Promoted ≥30d, övergivna
                            // PendingReview ≥90d) för GDPR Art. 5(1)(e). Efter backfill (05:00), före
                            // digest (06:00); DEK-fritt (rör ingen interaktiv yta). DisableConcurrentExecution-skyddad.

        // ADR 0080 Vag 4 PR-4b — Strong-digest-dispatch. Två kadenser, en cron var (cron = fönstret):
        // Daglig 06:00 UTC och Veckovis måndag 06:00 UTC. MEDVETET på morgonen, EFTER nattscannen
        // (03:20) och klar av hard-delete-klustret (04:00/04:30) — en Stark-match från i natt hamnar
        // i denna morgons digest, och digesten racear inte 03:xx/04:xx-jobben.
        manager.AddOrUpdate<DigestDispatchWorker>(
            RecurringJobIds.DigestDispatchDaily,
            job => job.RunDailyAsync(CancellationToken.None),
            Cron.Daily(6));  // 06:00 UTC — daglig digest, DisableConcurrentExecution(1800)-skyddad

        manager.AddOrUpdate<DigestDispatchWorker>(
            RecurringJobIds.DigestDispatchWeekly,
            job => job.RunWeeklyAsync(CancellationToken.None),
            Cron.Weekly(DayOfWeek.Monday, 6));  // Måndag 06:00 UTC — veckovis digest (civic-default kadens)

        // ADR 0064 — publik landing-stats pre-compute. Hot-path per ADR 0045
        // Beslut 1 klass (a). Var 5:e min UTC: räcker för att landingens
        // "newToday"-räknare ska upplevas live utan att slå mer än trivialt
        // mot DB:n (två COUNT-queries på indexerade kolumner ~46k aktiva rader).
        // Krockar med stream-cron (*/10) 6×/timme — acceptabelt eftersom
        // stream-cron är HTTP-bunden mot JobTech, inte DB-bunden.
        manager.AddOrUpdate<RefreshLandingStatsWorker>(
            RecurringJobIds.RefreshLandingStats,
            job => job.RunAsync(CancellationToken.None),
            "*/5 * * * *");

        // #560 (ADR 0091) — full SCB company-register population/refresh. Config-driven cron
        // (ScbRegister:SyncCadenceCron; default weekly Sat 06:00 UTC — the only ~11 h slot clear of a
        // nightly SCB update AND the 02–05 UTC contention window, #708 PR 2 / Klas-confirmed 2026-07-09).
        // Long-running (~11 h under the 6-calls/10-s throttle) → DisableConcurrentExecution(4h)-guarded.
        // When ScbRegister:Enabled=false the job is
        // still registered but the orchestrator no-ops (no SCB call, no cert) so the schedule stays
        // drift-free with the RecurringJobIds allowlist.
        manager.AddOrUpdate<ScbCompanyRegisterSyncWorker>(
            RecurringJobIds.SyncScbCompanyRegister,
            job => job.RunAsync(CancellationToken.None),
            scbOptions.Value.SyncCadenceCron);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
