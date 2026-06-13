using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Security.Jobs.BackfillFieldEncryption;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4 + C5) — Hangfire-orchestrator som driver lazy-
/// migreringen deterministiskt till 100 % ciphertext över de fyra user-ägda
/// PII-kolumnerna. Samma chassi som <c>HardDeleteAccountsJob</c> /
/// <c>PurgeStaleRawPayloadsJob</c>: orchestratorn håller loop +
/// cancel-token-management + progress-log + audit; implementation-detaljerna
/// (per-owner DEK, interceptor-interplay, forced-Modified) ligger i
/// <see cref="IFieldEncryptionBackfiller"/>-porten.
///
/// <para>
/// Bounded: yttre while-loop hämtar owner-batchar tills inga legacy-ägare
/// kvarstår. Varje batch krymper restmängden monotont (per-owner backfill
/// gör ägarens rader ciphertext) → konvergerar. Hård max-iterations-vakt som
/// defense mot oväntad non-convergence (logga + bryt; idempotent → nästa cron
/// plockar upp). Per-owner try/catch (paritet HardDeleteAccountsJob TD-25):
/// ett ägar-fel blockerar inte resten; <see cref="OperationCanceledException"/>
/// re-throw:as så shutdown-cancel ej sväljs. Idempotent — re-run efter full
/// backfill är no-op (porten rör bara legacy-on-disk-rader).
/// </para>
///
/// <para>
/// Fitness-snapshot + audit-rad skrivs ALLTID (även 0 ägare) — GDPR Art. 30
/// accountability. Cutover-flippen (Beslut 5 steg 3) är INTE detta jobbs
/// ansvar (separat Klas-STOPP). Cron 05:00 UTC (30-min padding efter
/// purge-stale-raw-payloads 04:30, RecurringJobRegistrar-konvention).
/// </para>
/// </summary>
public sealed partial class BackfillFieldEncryptionJob(
    IFieldEncryptionBackfiller backfiller,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    ILogger<BackfillFieldEncryptionJob> logger)
{
    private const int OwnerBatchSize = 100;
    private const int ProgressLogEvery = 25;

    // Defense mot non-convergence: vid OwnerBatchSize=100 räcker detta för
    // 1M ägare. En icke-krympande batch (samma ägare återkommer) bryter via
    // denna vakt → logga + audit + nästa cron plockar upp (idempotent).
    private const int MaxBatchIterations = 10_000;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();

        var processed = 0;
        var failed = 0;
        var iterations = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++iterations > MaxBatchIterations)
            {
                LogNonConvergence(logger, iterations, processed);
                break;
            }

            var owners = await backfiller.GetOwnersWithLegacyFieldsAsync(
                OwnerBatchSize, cancellationToken);

            if (owners.Count == 0)
                break;

            foreach (var jobSeekerId in owners)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await backfiller.BackfillOwnerAsync(jobSeekerId, cancellationToken);
                    processed++;

                    if (processed % ProgressLogEvery == 0)
                        LogProgress(logger, processed);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    LogOwnerFailed(logger, jobSeekerId, ex);
                }
            }
        }

        var remaining = await backfiller.CountRemainingLegacyAsync(cancellationToken);
        LogComplete(logger, processed, failed, remaining.Total);

        // Audit-wire (ADR 0035) — skriv alltid (även 0 ägare): GDPR Art. 30
        // "behandlingsaktivitet har körts" + fitness-snapshot.
        await auditor.RecordAsync(
            new FieldEncryptionBackfillRun(
                AggregateId: runId,
                OccurredAt: clock.UtcNow,
                OwnersProcessed: processed,
                OwnersFailed: failed,
                RemainingCoverLetter: remaining.CoverLetter,
                RemainingApplicationNoteContent: remaining.ApplicationNoteContent,
                RemainingFollowUpNote: remaining.FollowUpNote,
                RemainingResumeVersionContent: remaining.ResumeVersionContent),
            cancellationToken);
    }

    [LoggerMessage(EventId = 5601, Level = LogLevel.Information,
        Message = "BackfillFieldEncryptionJob: {Processed} ägare backfill:ade hittills.")]
    private static partial void LogProgress(ILogger logger, int processed);

    [LoggerMessage(EventId = 5602, Level = LogLevel.Information,
        Message = "BackfillFieldEncryptionJob: klart — {Processed} ägare backfill:ade ({Failed} misslyckades och plockas upp av nästa cron), {Remaining} legacy-fält kvar.")]
    private static partial void LogComplete(
        ILogger logger, int processed, int failed, long remaining);

    [LoggerMessage(EventId = 5603, Level = LogLevel.Error,
        Message = "BackfillFieldEncryptionJob: backfill misslyckades för JobSeekerId={JobSeekerId} — fortsätter med nästa ägare, denna plockas upp av nästa cron.")]
    private static partial void LogOwnerFailed(
        ILogger logger, Guid jobSeekerId, Exception exception);

    [LoggerMessage(EventId = 5604, Level = LogLevel.Warning,
        Message = "BackfillFieldEncryptionJob: non-convergence-vakt löste ut efter {Iterations} batchar ({Processed} processade) — bryter, nästa cron plockar upp. Utred om legacy-ägare återkommer.")]
    private static partial void LogNonConvergence(
        ILogger logger, int iterations, int processed);
}
