using System.Data;
using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.Admin.Services;

/// <summary>
/// The import-run records, read from the audit log (#1982, ADR 0159). The newest stream run of a source that
/// finished inside a window: its end (<c>occurred_at</c> is the run's completion) and its failed-event count.
///
/// <para>
/// <b>Why SQL.</b> The payload is a string mapped to jsonb, and filtering on a key inside it takes a provider
/// JSON operator, which AGENTS.md §2.1 keeps out of Application. Filtering in memory instead has two bad forms:
/// a <c>Take</c> before the filter can return only backfill and snapshot rows, and report "none recorded" for a
/// stream that ran a minute ago, and no <c>Take</c> loads every row of the window. The event type, the window,
/// the source and the job type are all in the query; the only index is on <c>occurred_at</c>, so the window is
/// what bounds the scan (and prunes partitions), and "nothing was ever written" reads a day of rows and no more.
/// The bound is a parameter from the caller's clock, never the database's.
/// </para>
///
/// <para>
/// A singleton: the sampler that calls it is one, so it opens a scope of its own for the one call it makes.
/// Both the key names and their casing are what <c>SystemEventAuditor</c> serialises (<c>JobAdsSynced</c>); a
/// test writes its rows through that auditor for exactly that reason.
/// </para>
/// </summary>
internal sealed class SqlImportRunRecords(IServiceScopeFactory scopes) : IImportRunRecords
{
    private const int CommandTimeoutSeconds = 10;

    private const string Sql = """
        SELECT occurred_at,
               CASE WHEN (payload->>'Errors') ~ '^[0-9]{1,9}$' THEN (payload->>'Errors')::int END AS errors
        FROM audit_log
        WHERE event_type = @eventType
          AND occurred_at >= @notBefore
          AND (payload->>'Source') = @source
          AND (payload->>'JobType') = @jobType
        ORDER BY occurred_at DESC
        LIMIT 1
        """;

    public async Task<StreamImportRun?> LatestStreamRunAsync(
        JobSource source, DateTimeOffset notBefore, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(Sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@eventType", NpgsqlDbType.Text, JobAdsSynced.EventTypeName);
        command.Parameters.AddWithValue("@jobType", NpgsqlDbType.Text, JobAdsSynced.StreamJobType);
        command.Parameters.AddWithValue("@source", NpgsqlDbType.Text, source.Value);
        command.Parameters.AddWithValue("@notBefore", NpgsqlDbType.TimestampTz, notBefore.UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        // A count that cannot be read is not turned into zero: the read fails, and the sampler shows the row as
        // unknown. A payload without the key is a row no current writer produces (JobAdsSynced always has Errors).
        var completedAt = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc));
        return reader.IsDBNull(1)
            ? throw new InvalidDataException("A stream run is recorded without a readable error count.")
            : new StreamImportRun(completedAt, reader.GetInt32(1));
    }
}
