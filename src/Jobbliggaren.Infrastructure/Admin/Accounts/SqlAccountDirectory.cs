using System.Data;
using System.Globalization;
using System.Text;
using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.Admin.Accounts;

/// <summary>
/// The <see cref="IAccountDirectory"/> implementation (ADR 0151): raw, parameterised, read-only SQL over
/// <c>identity."AspNetUsers"</c> joined to <c>job_seekers</c>, in the <c>CompanyRegisterSearchQuery</c> form.
/// One composer builds the counts, the page count and the page, so all three read the same predicate. The
/// address is bound only as a parameter value; the status names, the ordering and the role name are fixed
/// text or bound values, never the caller's input.
/// </summary>
internal sealed class SqlAccountDirectory(AppDbContext db, ILookupNormalizer normalizer) : IAccountDirectory
{
    /// <summary>A raw command does not take EF's timeout, so it sets its own (the house's 30 s ceiling).</summary>
    internal const int CommandTimeoutSeconds = 30;

    private const string Columns =
        "user_id, email, email_confirmed, is_admin, job_seeker_id, registered_at, deleted_at, status";

    public async Task<AccountDirectoryPage> SearchAsync(
        AccountDirectorySearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var connection = await OpenConnectionAsync(cancellationToken);
        var address = NormalizedAddress(search.Address);

        int total;
        await using (var count = Command(connection, address, search.Status, "SELECT count(*) FROM classified"))
        {
            total = Convert.ToInt32(
                await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        var entries = new List<AccountDirectoryEntry>();
        await using var page = PageCommand(connection, address, search);
        await using var reader = await page.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(await ReadEntryAsync(reader, cancellationToken));

        return new AccountDirectoryPage(entries, total);
    }

    public async Task<AccountStatusCounts> CountByStatusAsync(string? address, CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CountsCommand(connection, NormalizedAddress(address));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        // Every row takes one of the CASE's arms; a row that takes none is a state the rule does not know.
        if (reader.GetInt32(4) != 0)
            throw new InvalidOperationException("An account matched no status in the directory's rule.");

        return new AccountStatusCounts(
            Total: reader.GetInt32(0),
            Active: reader.GetInt32(1),
            PendingDeletion: reader.GetInt32(2),
            ProfileMissing: reader.GetInt32(3));
    }

    public async Task<AccountDirectoryEntry?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = Command(
            connection, address: null, status: null, $"SELECT {Columns} FROM classified", " WHERE user_id = @user_id");
        command.Parameters.AddWithValue("@user_id", NpgsqlDbType.Uuid, userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? await ReadEntryAsync(reader, cancellationToken) : null;
    }

    /// <summary>The page: the predicate, then a fixed ordering for the sort, then the bounds.</summary>
    internal NpgsqlCommand PageCommand(NpgsqlConnection connection, string? address, AccountDirectorySearch search)
    {
        var command = Command(
            connection,
            address,
            search.Status,
            $"SELECT {Columns} FROM classified",
            $" ORDER BY {Ordering(search.Sort)} LIMIT @limit OFFSET @offset");
        command.Parameters.AddWithValue("@limit", NpgsqlDbType.Integer, search.PageSize);
        command.Parameters.AddWithValue("@offset", NpgsqlDbType.Integer, (search.Page - 1) * search.PageSize);
        return command;
    }

    /// <summary>
    /// Every count in one pass. The fifth column counts rows no arm of the CASE takes; the reader requires it
    /// to be zero, so a state the rule does not know fails loudly instead of vanishing from every count.
    /// </summary>
    internal NpgsqlCommand CountsCommand(NpgsqlConnection connection, string? address) =>
        Command(
            connection,
            address,
            status: null,
            "SELECT count(*)::int, "
            + "count(*) FILTER (WHERE status = @active)::int, "
            + "count(*) FILTER (WHERE status = @pending_deletion)::int, "
            + "count(*) FILTER (WHERE status = @profile_missing)::int, "
            + "count(*) FILTER (WHERE status IS NULL)::int "
            + "FROM classified");

    /// <summary>
    /// The one place the statement is composed: the <c>classified</c> CTE, then the caller's select over it,
    /// the status filter and the caller's tail. An absent address or status writes no clause, and every value
    /// the text names is bound here.
    /// </summary>
    private NpgsqlCommand Command(
        NpgsqlConnection connection,
        string? address,
        AccountStatus? status,
        string select,
        string tail = "")
    {
        var sql = new StringBuilder(
            """
            WITH classified AS (
                SELECT
                    u.id AS user_id,
                    u.email AS email,
                    u.normalized_email AS sort_address,
                    u.email_confirmed AS email_confirmed,
                    EXISTS (
                        SELECT 1
                        FROM identity."AspNetUserRoles" ur
                        JOIN identity."AspNetRoles" r ON r.id = ur.role_id
                        WHERE ur.user_id = u.id AND r.normalized_name = @admin_role
                    ) AS is_admin,
                    js.id AS job_seeker_id,
                    js.created_at AS registered_at,
                    js.deleted_at AS deleted_at,
                    CASE
                        WHEN js.id IS NULL THEN @profile_missing
                        WHEN js.deleted_at IS NOT NULL THEN @pending_deletion
                        WHEN js.deleted_at IS NULL THEN @active
                    END AS status
                FROM identity."AspNetUsers" u
                LEFT JOIN public.job_seekers js ON js.user_id = u.id
            """);
        if (address is not null)
            sql.Append("\n    WHERE u.normalized_email LIKE @address ESCAPE '\\'");
        sql.Append("\n)\n").Append(select);
        if (status is not null)
            sql.Append(" WHERE status = @status");
        sql.Append(tail);

        var command = new NpgsqlCommand(sql.ToString(), connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@admin_role", NpgsqlDbType.Text, normalizer.NormalizeName(Roles.Admin));
        command.Parameters.AddWithValue("@active", NpgsqlDbType.Text, nameof(AccountStatus.Active));
        command.Parameters.AddWithValue("@pending_deletion", NpgsqlDbType.Text, nameof(AccountStatus.PendingDeletion));
        command.Parameters.AddWithValue("@profile_missing", NpgsqlDbType.Text, nameof(AccountStatus.ProfileMissing));
        if (address is not null)
            command.Parameters.AddWithValue("@address", NpgsqlDbType.Text, address);
        if (status is not null)
            command.Parameters.AddWithValue("@status", NpgsqlDbType.Text, status.Value.ToString());
        return command;
    }

    private static string Ordering(AccountSort sort) => sort switch
    {
        AccountSort.RegisteredNewest => "registered_at DESC NULLS LAST, user_id",
        AccountSort.RegisteredOldest => "registered_at ASC NULLS LAST, user_id",
        AccountSort.AddressAscending => "sort_address ASC NULLS LAST, user_id",
        AccountSort.AddressDescending => "sort_address DESC NULLS LAST, user_id",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "No ordering for this sort."),
    };

    /// <summary>
    /// The term as a LIKE pattern over Identity's own normalised address: the same fold Identity applies when
    /// it stores one, then LIKE's metacharacters escaped (backslash first) and a <c>%</c> on each side. A
    /// blank term is no filter.
    /// </summary>
    internal string? NormalizedAddress(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return null;
        var normalized = normalizer.NormalizeEmail(term.Trim());
        var escaped = normalized
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static async Task<AccountDirectoryEntry> ReadEntryAsync(
        NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        return new AccountDirectoryEntry(
            UserId: reader.GetGuid(0),
            Email: await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1),
            IsAdmin: reader.GetBoolean(3),
            EmailConfirmed: reader.GetBoolean(2),
            Status: Enum.Parse<AccountStatus>(reader.GetString(7)),
            JobSeekerId: await reader.IsDBNullAsync(4, cancellationToken) ? null : new JobSeekerId(reader.GetGuid(4)),
            RegisteredAt: await reader.IsDBNullAsync(5, cancellationToken)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(5),
            DeletedAt: await reader.IsDBNullAsync(6, cancellationToken)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(6));
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
