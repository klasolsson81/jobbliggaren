using System.Data;
using System.Globalization;
using System.Text.Json;
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
    /// Deregistered. NEVER hard-deletes. The caller MUST gate this behind the truncation + floor
    /// checks — a partial extract must never reach here. Returns rows de-registered.
    /// </summary>
    public async Task<int> DeregisterMissingAsync(
        DateTimeOffset runStartedAt, CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        // status is stored by NAME (varchar) — compare against the literal enum name.
        cmd.CommandText = """
            UPDATE company_register
            SET status = 'Deregistered'
            WHERE synced_at < @run_started_at AND status <> 'Deregistered';
            """;
        cmd.Parameters.AddWithValue("@run_started_at", NpgsqlDbType.TimestampTz, runStartedAt);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        string status);
}
