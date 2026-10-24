using System.Data;
using System.Net;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — the ingestion side of the stored DMARC reports: parameterised PostgreSQL against the
/// concrete <see cref="AppDbContext"/>'s connection (the <c>CompanyWatchCriterionMemberStore</c> form). Every write that
/// spans two rows is one transaction; a write that would move the cursor backwards, or end a turn the message is not
/// in, fails loud instead of being absorbed.
/// <para>
/// Nothing may touch EF's change tracker while one of these transactions is open: they are begun on the underlying
/// connection, so EF does not know they exist and a <c>SaveChangesAsync</c> would silently join them.
/// </para>
/// </summary>
internal sealed partial class SqlDmarcReportStore(AppDbContext db, ILogger<SqlDmarcReportStore> logger) : IDmarcReportStore
{
    /// <summary>A raw command does not inherit EF's timeout, so it is set on every command.</summary>
    internal const int CommandTimeoutSeconds = 30;

    public async ValueTask<DmarcRunState?> ReadStateAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        return await DmarcStateRow.ReadAsync(connection, transaction: null, CommandTimeoutSeconds, cancellationToken);
    }

    public async ValueTask EnsureStateAsync(DateTimeOffset attemptAt, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            transaction: null,
            """
            INSERT INTO public.dmarc_mailbox_state (id, last_attempt_at, last_outcome)
            VALUES (@id, @attempt_at, @incomplete)
            ON CONFLICT (id) DO NOTHING;
            """);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, DmarcStateRow.SingletonId);
        command.Parameters.AddWithValue("attempt_at", NpgsqlDbType.TimestampTz, attemptAt.ToUniversalTime());
        command.Parameters.AddWithValue("incomplete", NpgsqlDbType.Smallint, Number(DmarcRunOutcome.Incomplete));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<DmarcCrashSweep> SweepCrashesAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            transaction: null,
            """
            UPDATE public.dmarc_mailbox_messages
            SET outcome = CASE WHEN attempts >= @max_attempts THEN @abandoned ELSE @retry_pending END,
                attempts = CASE WHEN attempts >= @max_attempts THEN attempts ELSE attempts + 1 END
            WHERE outcome = @in_progress
            RETURNING outcome;
            """);
        command.Parameters.AddWithValue(
            "max_attempts", NpgsqlDbType.Smallint, (short)DmarcIngestionLimits.MaxCrashAttempts);
        command.Parameters.AddWithValue("abandoned", NpgsqlDbType.Smallint, Number(DmarcMessageOutcome.Abandoned));
        command.Parameters.AddWithValue("retry_pending", NpgsqlDbType.Smallint, Number(DmarcMessageOutcome.RetryPending));
        command.Parameters.AddWithValue("in_progress", NpgsqlDbType.Smallint, Number(DmarcMessageOutcome.InProgress));

        var requeued = 0;
        var abandoned = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetInt16(0) == Number(DmarcMessageOutcome.Abandoned))
                abandoned++;
            else
                requeued++;
        }

        return new DmarcCrashSweep(requeued, abandoned);
    }

    public async ValueTask<bool> EnterEpochAsync(long epoch, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1);
        var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long? previous;
        await using (var read = Command(
            connection,
            transaction,
            "SELECT uid_validity FROM public.dmarc_mailbox_state WHERE id = @id FOR UPDATE;"))
        {
            read.Parameters.AddWithValue("id", NpgsqlDbType.Integer, DmarcStateRow.SingletonId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("The DMARC state row must exist before the cursor enters an epoch.");
            previous = reader.IsDBNull(0) ? null : reader.GetInt64(0);
        }

        if (previous == epoch)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await using (var move = Command(
            connection,
            transaction,
            "UPDATE public.dmarc_mailbox_state SET uid_validity = @epoch, last_uid = 0 WHERE id = @id;"))
        {
            move.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epoch);
            move.Parameters.AddWithValue("id", NpgsqlDbType.Integer, DmarcStateRow.SingletonId);
            await move.ExecuteNonQueryAsync(cancellationToken);
        }

        if (previous is { } previousEpoch)
        {
            await using var forget = Command(
                connection,
                transaction,
                "DELETE FROM public.dmarc_mailbox_messages WHERE uid_validity = @previous;");
            forget.Parameters.AddWithValue("previous", NpgsqlDbType.Bigint, previousEpoch);
            await forget.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask ClaimAsync(
        long epoch, long position, DateTimeOffset receivedAt, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AdvanceAsync(connection, transaction, epoch, position, cancellationToken);

        await using (var claim = Command(
            connection,
            transaction,
            """
            INSERT INTO public.dmarc_mailbox_messages (uid_validity, uid, received_at, outcome, attempts, retries)
            VALUES (@epoch, @position, @received_at, @in_progress, 1, 0);
            """))
        {
            claim.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epoch);
            claim.Parameters.AddWithValue("position", NpgsqlDbType.Bigint, position);
            claim.Parameters.AddWithValue("received_at", NpgsqlDbType.TimestampTz, receivedAt.ToUniversalTime());
            claim.Parameters.AddWithValue("in_progress", NpgsqlDbType.Smallint, Number(DmarcMessageOutcome.InProgress));
            await claim.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask AdvanceCursorAsync(long epoch, long position, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await AdvanceAsync(connection, transaction: null, epoch, position, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<DmarcPendingMessage>> ListRetriesAsync(
        long epoch, int max, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(max);
        if (max == 0)
            return [];

        var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            transaction: null,
            """
            SELECT uid, received_at, retries
            FROM public.dmarc_mailbox_messages
            WHERE uid_validity = @epoch AND outcome = @retry_pending
            ORDER BY uid
            LIMIT @max;
            """);
        command.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epoch);
        command.Parameters.AddWithValue("retry_pending", NpgsqlDbType.Smallint, Number(DmarcMessageOutcome.RetryPending));
        command.Parameters.AddWithValue("max", NpgsqlDbType.Integer, max);

        var pending = new List<DmarcPendingMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pending.Add(new DmarcPendingMessage(
                reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetInt16(2)));
        }

        return pending;
    }

    public async ValueTask<bool> ClaimRetryAsync(long epoch, long position, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            transaction: null,
            """
            UPDATE public.dmarc_mailbox_messages
            SET outcome = CASE WHEN retries >= @retry_limit THEN @abandoned ELSE @in_progress END,
                retries = CASE WHEN retries >= @retry_limit THEN retries ELSE retries + 1 END
            WHERE uid_validity = @epoch AND uid = @position AND outcome = @retry_pending
            RETURNING outcome;
            """);
        command.Parameters.AddWithValue("retry_limit", NpgsqlDbType.Smallint, (short)DmarcIngestionLimits.RetryLimit);
        command.Parameters.AddWithValue("abandoned", NpgsqlDbType.Smallint, Number(DmarcMessageOutcome.Abandoned));
        command.Parameters.AddWithValue("in_progress", NpgsqlDbType.Smallint, Number(DmarcMessageOutcome.InProgress));
        command.Parameters.AddWithValue("retry_pending", NpgsqlDbType.Smallint, Number(DmarcMessageOutcome.RetryPending));
        command.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epoch);
        command.Parameters.AddWithValue("position", NpgsqlDbType.Bigint, position);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Only a message waiting for a retry can be claimed for one.");
        return reader.GetInt16(0) == Number(DmarcMessageOutcome.InProgress);
    }

    public async ValueTask SetOutcomeAsync(
        long epoch, long position, DmarcMessageOutcome outcome, CancellationToken cancellationToken)
    {
        if (!EndsATurnWithoutStoring(outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome), outcome, "Only a retry, a skip or a rejection ends a turn without storing.");
        }

        var connection = await OpenAsync(cancellationToken);
        await EndTurnAsync(connection, transaction: null, epoch, position, outcome, cancellationToken);
    }

    public async ValueTask<DmarcMessageOutcome> StoreAsync(
        long epoch, long position, DmarcAggregateReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var connection = await OpenAsync(cancellationToken);

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var outcome = await StoreWithinCapsAsync(connection, transaction, report, cancellationToken);
            await EndTurnAsync(connection, transaction, epoch, position, outcome, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return outcome;
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.StringDataRightTruncation
                                               or PostgresErrorCodes.CheckViolation
                                               or PostgresErrorCodes.InvalidTextRepresentation)
        {
            await EndTurnAsync(
                connection, transaction: null, epoch, position, DmarcMessageOutcome.RejectedByStore, cancellationToken);
            LogRefusedByStore(logger, report.Records.Count, ex.SqlState);
            return DmarcMessageOutcome.RejectedByStore;
        }
    }

    public async ValueTask RecordAttemptAsync(
        DateTimeOffset attemptAt, DmarcRunOutcome outcome, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(outcome) || outcome == DmarcRunOutcome.Incomplete)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome), outcome, "A finished run records how it ended; only a first run's start is incomplete.");
        }

        var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            transaction: null,
            """
            INSERT INTO public.dmarc_mailbox_state AS s (id, last_attempt_at, last_outcome, last_success_at)
            VALUES (@id, @attempt_at, @outcome, @success_at)
            ON CONFLICT (id) DO UPDATE SET
                last_attempt_at = EXCLUDED.last_attempt_at,
                last_outcome = EXCLUDED.last_outcome,
                last_success_at = COALESCE(EXCLUDED.last_success_at, s.last_success_at);
            """);
        var at = attemptAt.ToUniversalTime();
        command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, DmarcStateRow.SingletonId);
        command.Parameters.AddWithValue("attempt_at", NpgsqlDbType.TimestampTz, at);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Smallint, Number(outcome));
        command.Parameters.AddWithValue(
            "success_at", NpgsqlDbType.TimestampTz, outcome == DmarcRunOutcome.Succeeded ? at : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask AnalyzeAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = Command(
            connection,
            transaction: null,
            "ANALYZE public.dmarc_reports; ANALYZE public.dmarc_report_rows; ANALYZE public.dmarc_mailbox_messages;");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The day's caps, then the report and its records. The caps count what was received on the report's UTC day
    /// between two instants, never by casting to a date, which would read the session's time zone.
    /// </summary>
    private static async Task<DmarcMessageOutcome> StoreWithinCapsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, DmarcAggregateReport report,
        CancellationToken cancellationToken)
    {
        var received = report.ReceivedAt.ToUniversalTime();
        var dayStart = new DateTimeOffset(received.Year, received.Month, received.Day, 0, 0, 0, TimeSpan.Zero);

        await using (var caps = Command(
            connection,
            transaction,
            """
            SELECT
                (SELECT count(*) FROM public.dmarc_reports
                 WHERE received_at >= @day_start AND received_at < @day_end),
                (SELECT count(*) FROM public.dmarc_report_rows rr
                 JOIN public.dmarc_reports r ON r.id = rr.report_id
                 WHERE r.received_at >= @day_start AND r.received_at < @day_end);
            """))
        {
            caps.Parameters.AddWithValue("day_start", NpgsqlDbType.TimestampTz, dayStart);
            caps.Parameters.AddWithValue("day_end", NpgsqlDbType.TimestampTz, dayStart.AddDays(1));
            await using var reader = await caps.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var reportsToday = reader.GetInt64(0);
            var recordsToday = reader.GetInt64(1);
            if (reportsToday + 1 > DmarcIngestionLimits.MaxStoredReportsPerUtcDay
                || recordsToday + report.Records.Count > DmarcIngestionLimits.MaxStoredRecordsPerUtcDay)
                return DmarcMessageOutcome.VolumeCapped;
        }

        var reportId = Guid.NewGuid();
        var digest = report.CopyContentSha256();
        await using (var insert = Command(
            connection,
            transaction,
            """
            INSERT INTO public.dmarc_reports
                (id, reporter, reporter_report_id, content_sha256, begins_at, ends_at, received_at)
            VALUES (@id, @reporter, @reporter_report_id, @content_sha256, @begins_at, @ends_at, @received_at)
            ON CONFLICT (reporter, reporter_report_id, content_sha256) DO NOTHING;
            """))
        {
            insert.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, reportId);
            insert.Parameters.AddWithValue("reporter", NpgsqlDbType.Text, report.Reporter);
            insert.Parameters.AddWithValue("reporter_report_id", NpgsqlDbType.Text, report.ReportId);
            insert.Parameters.AddWithValue("content_sha256", NpgsqlDbType.Bytea, digest);
            insert.Parameters.AddWithValue("begins_at", NpgsqlDbType.TimestampTz, report.BeginsAt.ToUniversalTime());
            insert.Parameters.AddWithValue("ends_at", NpgsqlDbType.TimestampTz, report.EndsAt.ToUniversalTime());
            insert.Parameters.AddWithValue("received_at", NpgsqlDbType.TimestampTz, received);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 0)
                return DmarcMessageOutcome.Duplicate;
        }

        await InsertRecordsAsync(connection, transaction, reportId, report.Records, cancellationToken);

        await using var conflict = Command(
            connection,
            transaction,
            """
            SELECT EXISTS (
                SELECT 1 FROM public.dmarc_reports
                WHERE reporter = @reporter AND reporter_report_id = @reporter_report_id
                  AND content_sha256 <> @content_sha256);
            """);
        conflict.Parameters.AddWithValue("reporter", NpgsqlDbType.Text, report.Reporter);
        conflict.Parameters.AddWithValue("reporter_report_id", NpgsqlDbType.Text, report.ReportId);
        conflict.Parameters.AddWithValue("content_sha256", NpgsqlDbType.Bytea, digest);
        return await conflict.ExecuteScalarAsync(cancellationToken) is true
            ? DmarcMessageOutcome.StoredConflicting
            : DmarcMessageOutcome.Stored;
    }

    private static async Task InsertRecordsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid reportId,
        IReadOnlyList<DmarcReportRecord> records, CancellationToken cancellationToken)
    {
        var ordinals = new int[records.Count];
        var sources = new IPAddress[records.Count];
        var counts = new int[records.Count];
        var headerFroms = new string[records.Count];
        var dkim = new short[records.Count];
        var spf = new short[records.Count];
        for (var i = 0; i < records.Count; i++)
        {
            ordinals[i] = i;
            sources[i] = records[i].Source.Value;
            counts[i] = records[i].MessageCount;
            headerFroms[i] = records[i].HeaderFrom;
            dkim[i] = (short)records[i].Dkim;
            spf[i] = (short)records[i].Spf;
        }

        await using var insert = Command(
            connection,
            transaction,
            """
            INSERT INTO public.dmarc_report_rows
                (report_id, ordinal, source_ip, message_count, header_from, dkim_aligned, spf_aligned)
            SELECT @report_id, r.ordinal, r.source_ip, r.message_count, r.header_from, r.dkim_aligned, r.spf_aligned
            FROM unnest(@ordinals, @sources, @counts, @header_froms, @dkim, @spf)
                AS r(ordinal, source_ip, message_count, header_from, dkim_aligned, spf_aligned);
            """);
        insert.Parameters.AddWithValue("report_id", NpgsqlDbType.Uuid, reportId);
        insert.Parameters.AddWithValue("ordinals", NpgsqlDbType.Array | NpgsqlDbType.Integer, ordinals);
        insert.Parameters.AddWithValue("sources", NpgsqlDbType.Array | NpgsqlDbType.Inet, sources);
        insert.Parameters.AddWithValue("counts", NpgsqlDbType.Array | NpgsqlDbType.Integer, counts);
        insert.Parameters.AddWithValue("header_froms", NpgsqlDbType.Array | NpgsqlDbType.Text, headerFroms);
        insert.Parameters.AddWithValue("dkim", NpgsqlDbType.Array | NpgsqlDbType.Smallint, dkim);
        insert.Parameters.AddWithValue("spf", NpgsqlDbType.Array | NpgsqlDbType.Smallint, spf);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Moves the cursor forward within its epoch, and only forward.</summary>
    private static async Task AdvanceAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, long epoch, long position,
        CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            transaction,
            """
            UPDATE public.dmarc_mailbox_state SET last_uid = @position
            WHERE id = @id AND uid_validity = @epoch AND last_uid < @position;
            """);
        command.Parameters.AddWithValue("position", NpgsqlDbType.Bigint, position);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, DmarcStateRow.SingletonId);
        command.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epoch);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The cursor moves forward within the epoch it is in; this position is not after it.");
        }
    }

    /// <summary>Ends the turn of a claimed message; a message no run holds has no turn to end.</summary>
    private static async Task EndTurnAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, long epoch, long position,
        DmarcMessageOutcome outcome, CancellationToken cancellationToken)
    {
        await using var command = Command(
            connection,
            transaction,
            """
            UPDATE public.dmarc_mailbox_messages SET outcome = @outcome
            WHERE uid_validity = @epoch AND uid = @position AND outcome = @in_progress;
            """);
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Smallint, Number(outcome));
        command.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epoch);
        command.Parameters.AddWithValue("position", NpgsqlDbType.Bigint, position);
        command.Parameters.AddWithValue("in_progress", NpgsqlDbType.Smallint, Number(DmarcMessageOutcome.InProgress));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Only a claimed message's turn can end.");
    }

    private static bool EndsATurnWithoutStoring(DmarcMessageOutcome outcome) => outcome is
        DmarcMessageOutcome.RetryPending
        or DmarcMessageOutcome.SkippedTooLarge
        or DmarcMessageOutcome.SkippedNoReportPart
        or DmarcMessageOutcome.SkippedMultipleReportParts
        or DmarcMessageOutcome.SkippedForensic
        or DmarcMessageOutcome.SkippedUnsupportedEncoding
        or DmarcMessageOutcome.SkippedHeaderTooLarge
        or DmarcMessageOutcome.RejectedMalformed
        or DmarcMessageOutcome.RejectedPolicyDomain
        or DmarcMessageOutcome.RejectedContent;

    private static short Number(DmarcMessageOutcome outcome) => (short)outcome;

    private static short Number(DmarcRunOutcome outcome) => (short)outcome;

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = sql;
        return command;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        return connection;
    }

    [LoggerMessage(3130, LogLevel.Warning,
        "[DmarcReportStore] The database refused an accepted report of {Records} records (SQLSTATE {SqlState}); "
        + "its message is recorded as refused by the store")]
    private static partial void LogRefusedByStore(ILogger logger, int records, string sqlState);
}
