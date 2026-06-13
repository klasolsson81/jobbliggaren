namespace Jobbliggaren.Application.Common.Auditing;

/// <summary>
/// Port för partition-DDL-operationer mot audit_log-tabellen.
/// Implementeras i Infrastructure-lagret och anropas endast av
/// AuditLogRetentionJob (per ADR 0024 delbeslut 1).
///
/// Varför egen port: partition-DDL bryter EF Core-modellen — det är ren
/// PostgreSQL-administration, inte domain-mutation. Att isolera DDL-
/// anrop bakom en port håller orchestratorn testbar (mock-bar) och
/// disciplinerar bypass-ytan på samma sätt som IAuditTrailEraser
/// (ADR 0024 delbeslut 3). Architecture test verifierar att porten
/// bara refereras av AuditLogRetentionJob.
/// </summary>
public interface IAuditPartitionMaintainer
{
    /// <summary>
    /// Skapar morgondagens partition (audit_log_YYYYMMDD format) om den
    /// inte redan finns. Idempotent via PostgreSQL CREATE TABLE IF NOT EXISTS
    /// — om jobbet kör flera gånger samma dag skapas inget nytt.
    /// </summary>
    /// <param name="now">Aktuell tidpunkt (UTC) — partition-bound härleds som
    /// nästa kalenderdag i UTC.</param>
    /// <returns>Namn på partitionen som hanterades (skapad eller redan
    /// existerande). För logging.</returns>
    Task<string> EnsureNextDayPartitionAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Droppar audit_log-partitions vars upper bound (slutdatum) är äldre
    /// än cutoff. Default-partitionen påverkas aldrig.
    /// Idempotent — om inga partitions matchar returneras tom lista.
    /// </summary>
    /// <param name="cutoff">Partitions med upper bound &lt;= cutoff droppas.
    /// Per BUILD.md §7.1 ska retention vara 90 dagar — anroparen passerar
    /// (now - 90 days).</param>
    /// <returns>Namn på droppade partitions, för logging.</returns>
    Task<IReadOnlyList<string>> DropPartitionsOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
