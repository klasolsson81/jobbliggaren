using Jobbliggaren.Application.Admin.Email.Dmarc;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — the one read of <c>dmarc_mailbox_state</c>, shared by the ingestion store and the page's
/// read so both map it the same way. An outcome this release does not know fails the read rather than being shown as
/// something it is not.
/// </summary>
internal static class DmarcStateRow
{
    private const string Sql =
        """
        SELECT last_attempt_at, last_outcome, last_success_at, uid_validity, last_uid
        FROM public.dmarc_mailbox_state
        WHERE id = @id;
        """;

    internal const int SingletonId = 1;

    public static async ValueTask<DmarcRunState?> ReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText = Sql;
        command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, SingletonId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var outcome = (DmarcRunOutcome)reader.GetInt16(1);
        if (!Enum.IsDefined(outcome))
        {
            throw new InvalidOperationException(
                $"The DMARC state row holds run outcome {(int)outcome}, which this release does not know.");
        }

        var cursor = reader.IsDBNull(3)
            ? null
            : new DmarcCursor(reader.GetInt64(3), reader.GetInt64(4));

        return new DmarcRunState(
            reader.GetFieldValue<DateTimeOffset>(0),
            outcome,
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            cursor);
    }
}
