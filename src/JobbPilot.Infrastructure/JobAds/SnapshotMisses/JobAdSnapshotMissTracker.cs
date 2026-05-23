using System.Data;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace JobbPilot.Infrastructure.JobAds.SnapshotMisses;

/// <summary>
/// ADR 0032-amendment 2026-05-23 — <see cref="IJobAdSnapshotMissTracker"/>-
/// impl. Använder konkret <see cref="AppDbContext"/> + raw parametriserat
/// PostgreSQL (CLAUDE.md §5.4 förbjuder concatenation, inte parametriserat
/// SQL — och alternativet <c>ExecuteUpdate.Where(Contains(50k-strings))</c>
/// kan inte parametriseras effektivt av Npgsql).
/// <para>
/// Två kärn-operationer:
/// <list type="number">
/// <item><see cref="ApplyAsync"/> — efter komplett snapshot (ej trunkerad): upsert
/// miss-räknare för seen-set + increment för Active-rader vars external_id INTE
/// finns i seen-set.</item>
/// <item><see cref="ArchiveJobAdsWithMissCountAtLeastAsync"/> — bulk-arkivera
/// Active-rader vars miss-räknare passerat tröskeln. Body bypassar
/// <see cref="JobAd.Archive"/> och raisar inget per-item-event (CTO-rond
/// 2026-05-23 Q3=B; ingen subscriber finns idag, D8.a-verifiering).</item>
/// </list>
/// </para>
/// </summary>
internal sealed partial class JobAdSnapshotMissTracker(
    AppDbContext db,
    ILogger<JobAdSnapshotMissTracker> logger) : IJobAdSnapshotMissTracker
{
    public async Task<SnapshotMissUpdateResult> ApplyAsync(
        JobSource source,
        IReadOnlySet<string> seenExternalIds,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(seenExternalIds);

        var sourceValue = source.Value;
        // text[]-parameter krävs av Postgres unnest. Materialisera till array
        // — set:n är redan in-memory (~50k strängar, ~1.5 MB; ADR 0045-OK).
        var seenArray = seenExternalIds.ToArray();

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // (1) Upsert miss_count=0 för seenIds (sågs i komplett snapshot).
        // INSERT … ON CONFLICT DO UPDATE bevarar idempotens (Fowler 2002).
        int resetCount;
        await using (var resetCmd = connection.CreateCommand())
        {
            resetCmd.CommandText = """
                INSERT INTO job_ad_snapshot_misses (source, external_id, miss_count, first_missed_at, last_missed_at)
                SELECT @source, unnest(@seen_ids::text[]), 0, NULL, NULL
                ON CONFLICT (source, external_id) DO UPDATE
                  SET miss_count = 0, first_missed_at = NULL, last_missed_at = NULL;
                """;
            resetCmd.Parameters.AddWithValue("@source", NpgsqlDbType.Text, sourceValue);
            resetCmd.Parameters.AddWithValue("@seen_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, seenArray);
            resetCount = await resetCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // (2) Increment miss_count för Active-rader (deleted_at IS NULL,
        // source matchar) vars external_id INTE i seen-set. Använder
        // anti-join (NOT EXISTS) snarare än NOT IN (NULL-säkrare i SQL).
        int incrementedCount;
        await using (var incCmd = connection.CreateCommand())
        {
            incCmd.CommandText = """
                WITH missing AS (
                    SELECT j.external_id
                    FROM job_ads j
                    WHERE j.status = 'Active'
                      AND j.external_source = @source
                      AND j.external_id IS NOT NULL
                      AND j.deleted_at IS NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM unnest(@seen_ids::text[]) s(id)
                          WHERE s.id = j.external_id
                      )
                )
                INSERT INTO job_ad_snapshot_misses (source, external_id, miss_count, first_missed_at, last_missed_at)
                SELECT @source, m.external_id, 1, @observed_at, @observed_at FROM missing m
                ON CONFLICT (source, external_id) DO UPDATE
                  SET miss_count = job_ad_snapshot_misses.miss_count + 1,
                      last_missed_at = @observed_at,
                      first_missed_at = COALESCE(job_ad_snapshot_misses.first_missed_at, @observed_at);
                """;
            incCmd.Parameters.AddWithValue("@source", NpgsqlDbType.Text, sourceValue);
            incCmd.Parameters.AddWithValue("@seen_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, seenArray);
            incCmd.Parameters.AddWithValue("@observed_at", NpgsqlDbType.TimestampTz, observedAt);
            incrementedCount = await incCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        LogApplied(logger, sourceValue, seenArray.Length, resetCount, incrementedCount);
        return new SnapshotMissUpdateResult(resetCount, incrementedCount);
    }

    public async Task<int> ArchiveJobAdsWithMissCountAtLeastAsync(
        JobSource source,
        int threshold,
        DateTimeOffset archivedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (threshold < 1)
            throw new ArgumentOutOfRangeException(
                nameof(threshold), threshold, "Threshold måste vara >= 1.");

        // Bulk-UPDATE via EXISTS-join mot miss-tabellen. ExecuteUpdateAsync
        // respekterar global query-filter (DeletedAt IS NULL) per EF Core 8+
        // (verifierat i integration-test). SetProperty på SmartEnum-converter
        // fungerar med statisk readonly-värde JobAdStatus.Archived.
        //
        // Domain-event-bortfall accepterat (CTO 2026-05-23 Q3=B, D8.a — inga
        // subscribers på JobAdArchivedDomainEvent). Aggregerad audit-rad via
        // ISystemEventAuditor skrivs av caller.
        var archivedStatus = JobAdStatus.Archived;
        var rowsAffected = await db.JobAds
            .Where(j => j.Status == JobAdStatus.Active
                        && j.External != null
                        && j.External.Source == source
                        && db.Set<JobAdSnapshotMiss>().Any(m =>
                            m.Source == source.Value
                            && m.ExternalId == j.External.ExternalId
                            && m.MissCount >= threshold))
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, _ => archivedStatus),
                cancellationToken).ConfigureAwait(false);

        LogArchived(logger, source.Value, threshold, rowsAffected);
        return rowsAffected;
    }

    public async Task<int> CountActiveJobAdsAsync(
        JobSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Global query-filter (DeletedAt IS NULL) appliceras automatiskt på
        // db.JobAds. External != null säkerställer Manual-rader exkluderas
        // (de saknar External-VO). Paritet med ArchiveJobAdsWithMissCountAtLeastAsync.
        return await db.JobAds
            .Where(j => j.Status == JobAdStatus.Active
                        && j.External != null
                        && j.External.Source == source)
            .CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountArchiveCandidatesAsync(
        JobSource source,
        int threshold,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (threshold < 1)
            throw new ArgumentOutOfRangeException(
                nameof(threshold), threshold, "Threshold måste vara >= 1.");

        // Samma EXISTS-join som ArchiveJobAdsWithMissCountAtLeastAsync använder —
        // skall ge identisk count som faktiskt arkiveras av nästa anrop.
        return await db.JobAds
            .Where(j => j.Status == JobAdStatus.Active
                        && j.External != null
                        && j.External.Source == source
                        && db.Set<JobAdSnapshotMiss>().Any(m =>
                            m.Source == source.Value
                            && m.ExternalId == j.External.ExternalId
                            && m.MissCount >= threshold))
            .CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int?> GetMaxObservedSnapshotSizeAsync(
        JobSource source,
        int days,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (days < 1)
            throw new ArgumentOutOfRangeException(nameof(days), days, "days måste vara >= 1.");

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Läser System.JobAdsSynced-audit-rader för snapshot-jobbet senaste N dygn.
        // payload-jsonb innehåller serialiserade SystemAuditEvent-fält (Source,
        // JobType, Fetched, ...) per ADR 0035. MAX(Fetched) ger största
        // observerade snapshot-storlek → relativ floor-baslinje.
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT MAX((payload->>'Fetched')::int) AS max_fetched
            FROM audit_log
            WHERE event_type = 'System.JobAdsSynced'
              AND occurred_at >= now() - (@days || ' days')::interval
              AND (payload->>'Source') = @source
              AND (payload->>'JobType') = 'snapshot';
            """;
        cmd.Parameters.AddWithValue("@days", NpgsqlDbType.Text, days.ToString(System.Globalization.CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@source", NpgsqlDbType.Text, source.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    [LoggerMessage(EventId = 5601, Level = LogLevel.Information,
        Message = "JobAdSnapshotMissTracker.Apply: source={Source}, seen={SeenCount}, reset={ResetCount}, incremented={IncrementedCount}.")]
    private static partial void LogApplied(
        ILogger logger, string source, int seenCount, int resetCount, int incrementedCount);

    [LoggerMessage(EventId = 5602, Level = LogLevel.Information,
        Message = "JobAdSnapshotMissTracker.Archive: source={Source}, threshold={Threshold}, archived={Archived}.")]
    private static partial void LogArchived(
        ILogger logger, string source, int threshold, int archived);
}
