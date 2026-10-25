using System.Data;
using System.Net;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the page's read of the stored reports, in one repeatable-read snapshot so the state row,
/// the figures and the ledger counts describe the same moment. The conflict rule is declared once, in
/// <see cref="IncludedRecords"/>: a reporter and report id stored with more than one digest is judged over every
/// stored version, whatever the window, and none of its versions counts. A stored value this release does not know
/// fails the read rather than being counted around.
/// </summary>
internal sealed class SqlDmarcReportsSource(AppDbContext db) : IDmarcReportsSource
{
    private const int CommandTimeoutSeconds = 30;

    private const string IncludedRecords =
        """
        WITH in_window AS (
            SELECT r.id, r.reporter, r.reporter_report_id, r.begins_at, r.ends_at,
                EXISTS (
                    SELECT 1 FROM public.dmarc_reports other
                    WHERE other.reporter = r.reporter AND other.reporter_report_id = r.reporter_report_id
                      AND other.content_sha256 <> r.content_sha256) AS conflicting
            FROM public.dmarc_reports r
            WHERE r.begins_at < @window_end AND r.ends_at > @window_start
        ),
        included AS (
            SELECT id, reporter, begins_at, ends_at FROM in_window WHERE NOT conflicting
        ),
        records AS (
            SELECT rr.source_ip, rr.header_from, rr.message_count, rr.dkim_aligned, rr.spf_aligned
            FROM public.dmarc_report_rows rr
            JOIN included i ON i.id = rr.report_id
        )
        """;

    private const string TotalsSql = IncludedRecords +
        """
        SELECT
            (SELECT count(*) FROM included),
            (SELECT count(DISTINCT reporter) FROM included),
            (SELECT count(*) FROM (SELECT DISTINCT source_ip, header_from FROM records) distinct_sources),
            (SELECT coalesce(sum(message_count), 0) FROM records),
            (SELECT coalesce(sum(message_count), 0) FROM records
             WHERE dkim_aligned <> @pass AND spf_aligned <> @pass),
            (SELECT count(*) FROM (
                SELECT DISTINCT reporter, reporter_report_id FROM in_window WHERE conflicting) conflict_groups),
            (SELECT min(begins_at) FROM included),
            (SELECT max(ends_at) FROM included),
            (SELECT count(*) FROM records
             WHERE NOT (dkim_aligned = ANY (@alignments) AND spf_aligned = ANY (@alignments)));
        """;

    private const string SourcesSql = IncludedRecords +
        """
        SELECT source_ip, header_from, messages, dkim, spf, failing
        FROM (
            SELECT source_ip, header_from,
                sum(message_count) AS messages,
                coalesce(sum(message_count) FILTER (WHERE dkim_aligned = @pass), 0) AS dkim,
                coalesce(sum(message_count) FILTER (WHERE spf_aligned = @pass), 0) AS spf,
                coalesce(sum(message_count) FILTER (WHERE dkim_aligned <> @pass AND spf_aligned <> @pass), 0) AS failing
            FROM records
            GROUP BY source_ip, header_from
        ) grouped
        ORDER BY (failing > 0) DESC, failing DESC, messages DESC, family(source_ip), source_ip, header_from COLLATE "C"
        LIMIT @max_sources;
        """;

    private const string LedgerSql =
        """
        SELECT outcome, count(*)
        FROM public.dmarc_mailbox_messages
        WHERE received_at >= @window_start AND received_at < @window_end
        GROUP BY outcome
        ORDER BY outcome;
        """;

    private const string RetryPendingSql =
        "SELECT count(*) FROM public.dmarc_mailbox_messages WHERE outcome = @retry_pending;";

    private static readonly short[] KnownAlignments = [.. Enum.GetValues<DmarcAlignment>().Select(a => (short)a)];

    public async ValueTask<DmarcReportsRead> ReadAsync(
        DateTimeOffset windowStart, DateTimeOffset windowEnd, int maxSources, CancellationToken cancellationToken)
    {
        var start = windowStart.ToUniversalTime();
        var end = windowEnd.ToUniversalTime();
        if (end <= start)
            throw new ArgumentException("The window ends after it starts.", nameof(windowEnd));
        ArgumentOutOfRangeException.ThrowIfNegative(maxSources);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var snapshot =
            await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var state = await DmarcStateRow.ReadAsync(connection, snapshot, CommandTimeoutSeconds, cancellationToken);
        var totals = await ReadTotalsAsync(connection, snapshot, start, end, cancellationToken);
        var sources = await ReadSourcesAsync(connection, snapshot, start, end, maxSources, cancellationToken);
        var ledger = await ReadLedgerAsync(connection, snapshot, start, end, cancellationToken);

        long retryPending;
        await using (var pending = Command(connection, snapshot, RetryPendingSql))
        {
            pending.Parameters.AddWithValue(
                "retry_pending", NpgsqlDbType.Smallint, (short)DmarcMessageOutcome.RetryPending);
            retryPending = await pending.ExecuteScalarAsync(cancellationToken) is long count
                ? count
                : throw new InvalidOperationException("count(*) answered something other than a bigint.");
        }

        await snapshot.CommitAsync(cancellationToken);
        return new DmarcReportsRead(state, sources, totals, ledger, retryPending);
    }

    private static async Task<DmarcReportsTotals> ReadTotalsAsync(
        NpgsqlConnection connection, NpgsqlTransaction snapshot, DateTimeOffset start, DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, snapshot, TotalsSql);
        BindWindow(command, start, end);
        command.Parameters.AddWithValue("alignments", NpgsqlDbType.Array | NpgsqlDbType.Smallint, KnownAlignments);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        if (reader.GetInt64(8) > 0)
        {
            throw new InvalidOperationException(
                "A stored DMARC record holds an aligned result this release does not know.");
        }

        return new DmarcReportsTotals(
            Reports: reader.GetInt64(0),
            Reporters: reader.GetInt64(1),
            SourceCount: reader.GetInt64(2),
            Messages: reader.GetInt64(3),
            FailingMessages: reader.GetInt64(4),
            ConflictingReports: reader.GetInt64(5),
            CoveredFrom: reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            CoveredTo: reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7));
    }

    private static async Task<IReadOnlyList<DmarcSourceRow>> ReadSourcesAsync(
        NpgsqlConnection connection, NpgsqlTransaction snapshot, DateTimeOffset start, DateTimeOffset end,
        int maxSources, CancellationToken cancellationToken)
    {
        if (maxSources == 0)
            return [];

        await using var command = Command(connection, snapshot, SourcesSql);
        BindWindow(command, start, end);
        command.Parameters.AddWithValue("max_sources", NpgsqlDbType.Integer, maxSources);

        var rows = new List<DmarcSourceRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DmarcSourceRow(
                ReportedSourceAddress.FromStored(reader.GetFieldValue<IPAddress>(0)),
                reader.GetString(1),
                Messages: reader.GetInt64(2),
                DkimAligned: reader.GetInt64(3),
                SpfAligned: reader.GetInt64(4),
                FailingDmarc: reader.GetInt64(5)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<DmarcOutcomeCount>> ReadLedgerAsync(
        NpgsqlConnection connection, NpgsqlTransaction snapshot, DateTimeOffset start, DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, snapshot, LedgerSql);
        command.Parameters.AddWithValue("window_start", NpgsqlDbType.TimestampTz, start);
        command.Parameters.AddWithValue("window_end", NpgsqlDbType.TimestampTz, end);

        var counts = new List<DmarcOutcomeCount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var outcome = (DmarcMessageOutcome)reader.GetInt16(0);
            if (!Enum.IsDefined(outcome))
            {
                throw new InvalidOperationException(
                    $"A DMARC ledger row holds outcome {(int)outcome}, which this release does not know.");
            }

            counts.Add(new DmarcOutcomeCount(outcome, reader.GetInt64(1)));
        }

        return counts;
    }

    private static void BindWindow(NpgsqlCommand command, DateTimeOffset start, DateTimeOffset end)
    {
        command.Parameters.AddWithValue("window_start", NpgsqlDbType.TimestampTz, start);
        command.Parameters.AddWithValue("window_end", NpgsqlDbType.TimestampTz, end);
        command.Parameters.AddWithValue("pass", NpgsqlDbType.Smallint, (short)DmarcAlignment.Pass);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction snapshot, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = sql;
        return command;
    }
}
