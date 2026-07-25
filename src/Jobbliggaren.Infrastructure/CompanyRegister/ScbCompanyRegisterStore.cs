using System.Data;
using System.Globalization;
using System.Text.Json;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — persistence for the <c>company_register</c> read-model. Uses the concrete
/// <see cref="AppDbContext"/> + raw parametrized PostgreSQL (parity <c>JobAdSnapshotMissTracker</c>):
/// a per-row EF upsert would mean ~1M round-trips for a full population, so the bulk path is a batched
/// <c>INSERT … SELECT jsonb_to_recordset(@batch) … ON CONFLICT (organization_number) DO UPDATE</c>.
/// <c>jsonb_to_recordset</c> carries the whole batch (incl. the <c>text[]</c> SNI column) as ONE
/// parameter — no 65k-parameter ceiling, no per-row change-tracking.
///
/// <para>
/// The upsert also RESURRECTS: a row that reappears in a fresh extract is set back to its SCB status
/// (a previously de-registered company that SCB re-lists returns to Active). The deregister sweep only
/// ever flips <c>status</c> — it NEVER hard-deletes (company_watches point at the org.nr; ADR 0091).
/// </para>
/// </summary>
internal sealed class ScbCompanyRegisterStore(AppDbContext db)
{
    // #688 — explicit per-command timeouts on the population path (raw NpgsqlCommands get the
    // connection-string default of 30 s; an EF-level SetCommandTimeout would not reach them). The
    // 2000-row jsonb upsert exceeded 30 s under DB contention on the first live run, and the
    // full-table sweep UPDATE over ~1.17M rows can exceed 30 s even on a healthy isolated run.
    // Reviewed constants, not config (ADR 0091 amendment 2026-07-05 #688); never 0/infinite —
    // a genuinely hung command must still fail loud.
    internal const int CommandTimeoutSeconds = 120;
    internal const int SweepCommandTimeoutSeconds = 600;

    private static readonly JsonSerializerOptions BatchJson = new();

    /// <summary>
    /// Upserts one batch keyed on <c>organization_number</c>. <paramref name="syncedAt"/> stamps
    /// <c>synced_at</c> (the sweep predicate) on every touched row and <c>created_at</c> on inserts
    /// only. Returns rows affected (inserts + updates).
    /// </summary>
    public async Task<int> UpsertBatchAsync(
        IReadOnlyList<ScbCompanyRegisterEntry> batch,
        DateTimeOffset syncedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Count == 0)
            return 0;

        // Serialize the batch to a JSON array whose keys match the recordset column list below. The
        // SNI list becomes a JSON array → text[] via jsonb_to_recordset's typed column.
        var payload = JsonSerializer.Serialize(
            batch.Select(e => new BatchRow(
                e.OrganizationNumber, e.Name, e.SeatMunicipalityCode, e.SeatMunicipalityName,
                e.SniCodes, e.HasAdvertisingBlock, e.ScbStatusRaw, e.Status.ToString())),
            BatchJson);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = """
            INSERT INTO company_register (
                organization_number, company_name, sate_kommun_code, sate_kommun_name,
                sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at)
            SELECT
                r.organization_number, r.company_name, r.sate_kommun_code, r.sate_kommun_name,
                COALESCE(r.sni_codes, '{}'::text[]), r.reklamsparr, r.scb_status_raw, r.status,
                @synced_at, @synced_at
            FROM jsonb_to_recordset(@batch::jsonb) AS r(
                organization_number text, company_name text, sate_kommun_code text,
                sate_kommun_name text, sni_codes text[], reklamsparr boolean,
                scb_status_raw text, status text)
            ON CONFLICT (organization_number) DO UPDATE SET
                company_name    = EXCLUDED.company_name,
                sate_kommun_code = EXCLUDED.sate_kommun_code,
                sate_kommun_name = EXCLUDED.sate_kommun_name,
                sni_codes       = EXCLUDED.sni_codes,
                reklamsparr     = EXCLUDED.reklamsparr,
                scb_status_raw  = EXCLUDED.scb_status_raw,
                status          = EXCLUDED.status,
                synced_at       = @synced_at;
            """;
        cmd.Parameters.AddWithValue("@batch", NpgsqlDbType.Jsonb, payload);
        cmd.Parameters.AddWithValue("@synced_at", NpgsqlDbType.TimestampTz, syncedAt);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Flips every Active row NOT touched by this run (<c>synced_at &lt; runStartedAt</c>) to
    /// Deregistered, EXCEPT rows inside a protected (kommun, SNI) partition (#640, partition-scoped
    /// sweep). NEVER hard-deletes. The caller MUST gate this behind the truncation + floor checks — a
    /// partial extract must never reach here. Returns rows de-registered.
    ///
    /// <para>
    /// #640: <paramref name="protectedPartitions"/> are the over-cap 5-digit tails the run could only
    /// partially fetch. The (kommun, SNI) codes are unzipped HERE — never passed as two pre-split arrays —
    /// so the two SQL arrays are guaranteed non-empty together or empty together (a kommun list without its
    /// paired SNI list would collapse the protection to a no-op and false-deregister the very rows it must
    /// shield). An empty collection → two empty arrays → the exclusion predicate degenerates to a no-op
    /// (<c>ANY('{}')</c> is false), i.e. an unrestricted sweep (#628 back-compat). The arrays are bound
    /// as an EXPLICIT non-null empty <c>text[]</c>: a NULL array would make the predicate NULL and
    /// silently deregister NOTHING (fail-closed but a mystery), so it is bound explicitly.
    /// </para>
    ///
    /// <para>
    /// The exclusion protects the CROSS-PRODUCT <c>(sate_kommun_code ∈ kommun) AND (sni_codes ⋂ sni)</c>,
    /// a superset of the exact protected pairs. Over-protection only leaves some dead rows Active longer
    /// (self-healing on the next clean run) — it can NEVER false-deregister a live company. The <c>AND</c>
    /// (not <c>OR</c>) keeps the shield tight enough that the sweep still runs across the dense-metro cell's
    /// other SNI codes and every other municipality.
    /// </para>
    /// </summary>
    public async Task<int> DeregisterMissingAsync(
        DateTimeOffset runStartedAt,
        IReadOnlyCollection<ScbProtectedPartition> protectedPartitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protectedPartitions);

        // Unzip HERE (desync-proof): both arrays are non-empty together or empty together.
        var protectedKommun = protectedPartitions.Select(p => p.SeatMunicipalityCode).Distinct().ToArray();
        var protectedSni = protectedPartitions.Select(p => p.SniCode).Distinct().ToArray();

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = SweepCommandTimeoutSeconds;
        // status is stored by NAME (varchar) — compare against the literal enum name.
        cmd.CommandText = """
            UPDATE company_register
            SET status = 'Deregistered'
            WHERE synced_at < @run_started_at AND status <> 'Deregistered'
              AND NOT (sate_kommun_code = ANY(@protected_kommun) AND sni_codes && @protected_sni);
            """;
        cmd.Parameters.AddWithValue("@run_started_at", NpgsqlDbType.TimestampTz, runStartedAt);
        // Explicit non-null empty text[] when there is nothing to protect (never bind DBNull — see the doc).
        cmd.Parameters.Add(new NpgsqlParameter("@protected_kommun", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = protectedKommun,
        });
        cmd.Parameters.Add(new NpgsqlParameter("@protected_sni", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = protectedSni,
        });
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the planner's statistics for <c>company_register</c>. <b>This docblock is the canonical
    /// home for the argument</b> — CLAUDE.md §3.6 states the rule in one line and
    /// <c>docs/runbooks/scb-live-population.md</c> §7 covers the operational half; both point here
    /// rather than restating it.
    ///
    /// <para>
    /// The rule is the PostgreSQL manual's own (<i>Performance Tips → Populating a Database → "Run
    /// ANALYZE Afterwards"</i>): whatever bulk-loads a table refreshes its statistics, because the
    /// loader is the one process that knows the contents just changed wholesale. Plain <c>ANALYZE</c>,
    /// this table only, schema-qualified to match the runbook's diagnostics; never <c>VACUUM ANALYZE</c>
    /// (vacuuming is autovacuum's concern and a different change-reason).
    /// </para>
    ///
    /// <para>
    /// <b>Why autovacuum cannot be the guarantee.</b> Note this is a claim about the GUARANTEE, not an
    /// explanation of the state measured below. Its analyze trigger is change-driven —
    /// <c>n_mod_since_analyze</c> against <c>50 + 0.1 × reltuples</c> — and those counters live in the
    /// CUMULATIVE statistics, which PostgreSQL discards on an unclean shutdown and which neither
    /// <c>pg_upgrade</c> nor <c>pg_dump</c> carries. This register is written by ONE periodic job and
    /// read-only in between, so a reset in that window zeroes the counter with no subsequent DML left to
    /// re-arm it — nothing re-analyses it until the next sync, a week at the configured cadence. A table
    /// under continuous DML (<c>job_ads</c>) self-heals from the same reset; this one cannot. An explicit
    /// ANALYZE at the end of the load does not consult those counters at all.
    /// </para>
    ///
    /// <para>
    /// <b>Two different statistics systems, and they behave differently</b> — verified against the local
    /// PostgreSQL 18.3 binaries 2026-07-25, because an earlier draft of this comment got it wrong.
    /// <i>Cumulative</i> statistics (<c>last_analyze</c>, <c>n_mod_since_analyze</c> — autovacuum's
    /// trigger) survive none of the three events above. <i>Optimizer</i> statistics
    /// (<c>pg_statistic</c>/<c>pg_stats</c> — what the planner actually reads) are a WAL-logged catalog:
    /// they survive a crash, PG18's <c>pg_upgrade</c> carries them BY DEFAULT (<c>--no-statistics</c>
    /// opts out), and <c>pg_dump</c> omits them unless <c>--statistics</c> is passed.
    /// </para>
    ///
    /// <para>
    /// <b>The measured state, recorded without a mechanism.</b> On the dev register 2026-07-25:
    /// 1 066 938 rows and ZERO rows in <c>pg_stats</c> — never analysed — which is what made the register
    /// search walk the wrong index at 1 681–6 268 ms (#560, ADR 0119). Why autoanalyze never fired
    /// through the ~11 h population is NOT established: <c>reloptions</c> on the table is empty,
    /// <c>autovacuum</c> and <c>track_counts</c> are both on. The emptiness is therefore an observation,
    /// not evidence for the paragraph above — which stands on its own regardless.
    /// </para>
    ///
    /// <para>
    /// Cost: <b>871 ms</b> at 1 066 938 rows against the <see cref="CommandTimeoutSeconds"/> ceiling — a
    /// 137× margin, so the ordinary timeout applies and the sweep's raised one is not warranted. ANALYZE
    /// takes <c>SHARE UPDATE EXCLUSIVE</c>: it blocks neither reads nor writes, but it does conflict with
    /// a concurrent VACUUM/ANALYZE and with DDL.
    /// </para>
    /// </summary>
    public async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = "ANALYZE public.company_register;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The largest <c>TotalRowsFetched</c> recorded in prior <c>System.CompanyRegisterSynced</c> audit
    /// rows within the window — the relative-floor baseline for the sweep (parity
    /// <c>GetMaxObservedSnapshotSizeAsync</c>). Null when there is no prior run.
    /// </summary>
    public async Task<int?> GetMaxObservedTotalRowsFetchedAsync(
        int days, CancellationToken cancellationToken)
    {
        if (days < 1)
            throw new ArgumentOutOfRangeException(nameof(days), days, "days måste vara >= 1.");

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = """
            SELECT MAX((payload->>'TotalRowsFetched')::int) AS max_fetched
            FROM audit_log
            WHERE event_type = 'System.CompanyRegisterSynced'
              AND occurred_at >= now() - (@days || ' days')::interval;
            """;
        cmd.Parameters.AddWithValue("@days", NpgsqlDbType.Text, days.ToString(CultureInfo.InvariantCulture));
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    // The jsonb_to_recordset row shape. Property names must match the recordset column names.
    private sealed record BatchRow(
        string organization_number,
        string company_name,
        string sate_kommun_code,
        string? sate_kommun_name,
        IReadOnlyList<string> sni_codes,
        bool reklamsparr,
        string? scb_status_raw,
        string status)
    {
        // REDACTED (#883). The member names are snake_case because jsonb_to_recordset matches recordset
        // columns by property NAME (they cannot be renamed) — but the compiler-generated ToString() then
        // prints organization_number for a plain {X} MEL placeholder. The register is legal-entities-only
        // (ADR 0091) so this org.nr is not a personnummer, but redact defense-in-depth anyway (§5). Keeps
        // company_name; pinned by OrgNrRecordLoggingGuardTests.
        public override string ToString() => $"BatchRow({company_name}, org.nr redacted)";
    }
}
