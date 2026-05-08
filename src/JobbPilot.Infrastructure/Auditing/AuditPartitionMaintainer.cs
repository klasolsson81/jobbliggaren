using System.Globalization;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Infrastructure.Auditing;

/// <summary>
/// PostgreSQL-implementation av <see cref="IAuditPartitionMaintainer"/>.
/// Anropar partition-DDL via <see cref="DbContext.Database"/>.
/// Audit-bypass-pattern (DDL-anrop på audit_log) — porten är architektur-låst
/// till AuditLogRetentionJob via arch-test (ADR 0024 delbeslut 1 + 3).
///
/// Implementationen injicerar konkret <see cref="AppDbContext"/> istället för
/// <see cref="JobbPilot.Application.Common.Abstractions.IAppDbContext"/> eftersom
/// <c>Database</c>-facaden inte exponeras på interfacet — det är medvetet (raw
/// SQL ska inte vara tillgängligt från Application-lagret). Infrastructure-
/// impl:n är rätt hem för DDL-anrop.
/// </summary>
public sealed class AuditPartitionMaintainer(AppDbContext db) : IAuditPartitionMaintainer
{
    private const string PartitionNamePrefix = "audit_log_";
    private const string PartitionDateFormat = "yyyyMMdd";
    private const string PartitionBoundFormat = "yyyy-MM-dd 00:00:00+00";

    public async Task<string> EnsureNextDayPartitionAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var nextDay = now.UtcDateTime.Date.AddDays(1);
        var dayAfter = nextDay.AddDays(1);

        var partitionName = PartitionNamePrefix + nextDay.ToString(PartitionDateFormat, CultureInfo.InvariantCulture);
        var fromBound = nextDay.ToString(PartitionBoundFormat, CultureInfo.InvariantCulture);
        var toBound = dayAfter.ToString(PartitionBoundFormat, CultureInfo.InvariantCulture);

        // CREATE TABLE IF NOT EXISTS … PARTITION OF stöds av PG18 — om
        // partitionen redan finns emit:as bara NOTICE, inget fel. Idempotent
        // även vid flera körningar samma dag.
        //
        // SQL-injection: partitionName, fromBound, toBound är 100% derivat av
        // C#-konstruerade datum (DateTimeOffset.UtcDateTime + InvariantCulture-
        // formatterare). Inga user-inputs. Direkt string-interpolation OK eftersom
        // EF FormattableString-overload av ExecuteSqlAsync inte stödjer DDL-
        // identifierare (tabellnamn) som parametrar.
#pragma warning disable EF1002 // interpolated SQL — verifierat säkert ovan
#pragma warning disable EF1003 // concatenated SQL — verifierat säkert ovan
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE IF NOT EXISTS {partitionName} PARTITION OF audit_log " +
            $"FOR VALUES FROM ('{fromBound}') TO ('{toBound}');",
            cancellationToken);
#pragma warning restore EF1003
#pragma warning restore EF1002

        return partitionName;
    }

    public async Task<IReadOnlyList<string>> DropPartitionsOlderThanAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        // Lexikografisk jämförelse på partition-namn fungerar som datum-
        // jämförelse när alla namn följer audit_log_YYYYMMDD-mönstret
        // (8-tecken fixed-width). Default-partitionen filtreras bort av
        // regex-checken (^audit_log_[0-9]{8}$).
        //
        // Semantik: en partition vars datum är < cutoff_date har upper bound
        // <= cutoff och innehåller bara rader äldre än retention-fönstret.
        // Gränsfall (partition_date == cutoff_date) behålls — sista dygnet
        // bevaras tills nästa retention-körning.
        var cutoffName = PartitionNamePrefix + cutoff.UtcDateTime.Date.ToString(PartitionDateFormat, CultureInfo.InvariantCulture);

        // SqlQueryRaw med string.Format-syntax: regex-quantifier {8} måste
        // escapas som {{8}} eftersom EF tolkar enkla braces som format-
        // placeholders. Parameter {0} = cutoffName (säker — bind:as som
        // SQL-parameter, inte interpolation).
        //
        // FormattableString-overload (SqlQuery<T>) övervägdes men returnerar
        // tom rad-set i denna query — sannolikt EF-projection-issue mot
        // pg_class:s name-typ. Defererad som tech-debt; SqlQueryRaw fungerar.
        var oldPartitions = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.relname AS "Value"
                FROM pg_inherits i
                JOIN pg_class c ON c.oid = i.inhrelid
                WHERE inhparent = 'audit_log'::regclass
                  AND c.relname ~ '^audit_log_[0-9]{{8}}$'
                  AND c.relname < {0}
                ORDER BY c.relname
                """,
                cutoffName)
            .ToListAsync(cancellationToken);

        // DROP TABLE IF EXISTS per partition. Fail-fast: vid första PostgresException
        // bubblar exception till anroparen och resten av loopen körs aldrig.
        // Det är medveten default — Hangfire kommer retrya hela jobbet, och om
        // problemet är persistent (t.ex. lock-konflikt) ska ops upptäcka det
        // via dashboard/logg, inte via tyst skip-och-fortsätt-semantik.
        foreach (var partitionName in oldPartitions)
        {
#pragma warning disable EF1002 // interpolated SQL — partitionName från pg_class, INTE user input
            await db.Database.ExecuteSqlRawAsync(
                $"DROP TABLE IF EXISTS {partitionName};",
                cancellationToken);
#pragma warning restore EF1002
        }

        return oldPartitions;
    }
}
