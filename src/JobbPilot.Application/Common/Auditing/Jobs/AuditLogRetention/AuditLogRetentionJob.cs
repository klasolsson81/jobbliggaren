using JobbPilot.Domain.Common;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Application.Common.Auditing.Jobs.AuditLogRetention;

/// <summary>
/// Schemalagt retention-jobb för audit_log-tabellen. Skapar morgondagens
/// partition (rolling buffer framåt) + droppar partitions äldre än 90 dagar
/// (Art. 5(1)(e) Storage Limitation per BUILD.md §7.1 + ADR 0024 D1).
///
/// Registreras som Hangfire <c>RecurringJob</c> i Worker (cron 03:00 UTC,
/// före DELETE /me-relaterade jobb i 10b). Idempotent — kan köras flera
/// gånger samma dag utan biverkningar (CREATE TABLE IF NOT EXISTS +
/// DROP TABLE IF EXISTS).
///
/// Audit-paritet: ingen audit-rad skrivs av detta jobb. Partition-DDL är
/// ren ops, inte domain-mutation. Self-referential audit (audit-skrivning
/// av audit-retention) hade gett oändlig recursion vid kraschad audit_log.
/// </summary>
public sealed partial class AuditLogRetentionJob(
    IAuditPartitionMaintainer maintainer,
    IDateTimeProvider clock,
    ILogger<AuditLogRetentionJob> logger)
{
    /// <summary>
    /// 90-dagars retention per BUILD.md §7.1 + ADR 0022 (Art. 5(1)(e) Storage
    /// Limitation). Hardcoded i Fas 1 — flippas till IOptions om GDPR-
    /// myndighetstolkning skulle förändras eller om Klas vill driva olika
    /// retention per miljö (osannolikt).
    /// </summary>
    private const int RetentionDays = 90;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // 1. Säkerställ morgondagens partition. Idempotent — om den redan
        //    skapats (t.ex. av bootstrap-fönstret från migrationen, eller
        //    av en tidigare körning samma dag) returneras bara namnet.
        var ensured = await maintainer.EnsureNextDayPartitionAsync(now, cancellationToken);
        LogPartitionEnsured(logger, ensured);

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Droppa partitions äldre än retention-fönstret. Default-partitionen
        //    påverkas inte (regex i implementationen filtrerar bort den).
        var cutoff = now.AddDays(-RetentionDays);
        var dropped = await maintainer.DropPartitionsOlderThanAsync(cutoff, cancellationToken);

        if (dropped.Count == 0)
        {
            LogNoPartitionsToDrop(logger, cutoff);
        }
        else
        {
            // Per-partition-log + summary. Loop framför string.Join eftersom
            // CA1873 kräver att inga argument evalueras när logging är disabled.
            foreach (var partition in dropped)
                LogPartitionDropped(logger, partition);
            LogPartitionsDroppedSummary(logger, dropped.Count, cutoff);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "AuditLogRetentionJob: säkerställde partition {PartitionName}")]
    private static partial void LogPartitionEnsured(ILogger logger, string partitionName);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "AuditLogRetentionJob: inga partitions att droppa (cutoff {Cutoff:yyyy-MM-dd})")]
    private static partial void LogNoPartitionsToDrop(ILogger logger, DateTimeOffset cutoff);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "AuditLogRetentionJob: droppade partition {PartitionName}")]
    private static partial void LogPartitionDropped(ILogger logger, string partitionName);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "AuditLogRetentionJob: droppade {Count} partitions äldre än {Cutoff:yyyy-MM-dd}")]
    private static partial void LogPartitionsDroppedSummary(ILogger logger, int count, DateTimeOffset cutoff);
}
