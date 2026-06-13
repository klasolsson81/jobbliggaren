using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;

/// <summary>
/// Schemalagt orchestrator-jobb som hard-deletar konton vars 30-dagars
/// restore-fönster gått ut (ADR 0024 D6 + GDPR Art. 17).
///
/// Tre-stegs-algoritm:
/// 1. Steg 0 — Orphan-cleanup (Identity-rader utan matchande JobSeeker)
/// 2. Steg 1 — Hämta soft-deletade JobSeekers äldre än cutoff (= now − 30d)
/// 3. Steg 2 — Per JobSeeker: anonymize audit + hard-delete cascade
///    (transactional) + Identity-DELETE (separat boundary)
///
/// Implementation-detaljer ligger i <see cref="IAccountHardDeleter"/>-port.
/// Orchestratorn håller bara loop + cancel-token-management + progress-log.
///
/// Cron 04:00 UTC daily (1h efter retention/detect-ghosted) per ADR 0024 D6.
/// Idempotent — failed run plockas upp av nästa cron-körning utan biverkningar.
/// </summary>
public sealed partial class HardDeleteAccountsJob(
    IAccountHardDeleter hardDeleter,
    IDateTimeProvider clock,
    ILogger<HardDeleteAccountsJob> logger)
{
    /// <summary>
    /// 30-dagars restore-fönster per ADR 0024 D5. Användaren har 30 dagar
    /// från soft-delete att kontakta support för återställning innan kontot
    /// hard-deletas permanent. Hardcoded i Fas 1 — flippas till IOptions
    /// om policy förändras.
    /// </summary>
    private const int RestoreWindowDays = 30;

    private const int ProgressLogEvery = 25;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Steg 0 — Orphan-cleanup. Skyddar mot Identity-rader som hängde kvar
        // efter tidigare körning där Steg 2 h failade.
        var orphansCleaned = await hardDeleter.CleanupIdentityOrphansAsync(cancellationToken);
        LogOrphansCleaned(logger, orphansCleaned);

        cancellationToken.ThrowIfCancellationRequested();

        // Steg 1 — Hämta mogna konton.
        var cutoff = now.AddDays(-RestoreWindowDays);
        var jobSeekerIds = await hardDeleter.GetAccountsReadyForHardDeleteAsync(cutoff, cancellationToken);

        LogAccountsFound(logger, jobSeekerIds.Count, cutoff);

        if (jobSeekerIds.Count == 0)
        {
            LogComplete(logger, 0, 0);
            return;
        }

        // Steg 2 — Per-account hard-delete. Per-id loop matchar audit-paritet-
        // mönstret från DetectGhostedApplicationsJob (ADR 0023): isolering per
        // konto, en failure rullar inte tillbaka andra.
        //
        // TD-25: per-konto try/catch så ett enskilt fel (korrupt data, transient
        // DB-fel, Identity-API-fel) inte blockerar efterföljande konton i samma
        // körning. Misslyckade konton plockas upp av nästa cron-körning (jobbet
        // är idempotent). OperationCanceledException re-throw:as så
        // shutdown-cancel inte sväljs.
        var processed = 0;
        var failed = 0;
        foreach (var jobSeekerId in jobSeekerIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await hardDeleter.HardDeleteAccountAsync(jobSeekerId, cancellationToken);
                processed++;

                if (processed % ProgressLogEvery == 0)
                    LogProgress(logger, processed, jobSeekerIds.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                LogAccountFailed(logger, jobSeekerId, ex);
            }
        }

        LogComplete(logger, processed, failed);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "HardDeleteAccountsJob: rensade {Count} Identity-orphans (Steg 0)")]
    private static partial void LogOrphansCleaned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "HardDeleteAccountsJob: hittade {Count} konton mogna för hard-delete (cutoff {Cutoff:yyyy-MM-dd})")]
    private static partial void LogAccountsFound(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "HardDeleteAccountsJob: {Processed}/{Total} konton hard-deletade")]
    private static partial void LogProgress(ILogger logger, int processed, int total);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "HardDeleteAccountsJob: klart — {Processed} konton hard-deletade ({Failed} misslyckades och plockas upp av nästa cron)")]
    private static partial void LogComplete(ILogger logger, int processed, int failed);

    [LoggerMessage(EventId = 2502, Level = LogLevel.Error,
        Message = "HardDeleteAccountsJob: hard-delete misslyckades för JobSeekerId={JobSeekerId} — fortsätter med nästa konto, denna plockas upp av nästa cron")]
    private static partial void LogAccountFailed(ILogger logger, Guid jobSeekerId, Exception exception);
}
