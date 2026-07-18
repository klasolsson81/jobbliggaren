using System.Data;
using System.Text;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 company-search wave — the <see cref="ICompanyRegisterSearchQuery"/> implementation:
/// paginated search over the local <c>company_register</c> replica with every axis optional.
/// Raw parametrized SQL against the concrete <see cref="AppDbContext"/>, exactly like
/// <see cref="CompanyWatchBrowseQuery"/> (the register is Infrastructure-internal — NOT a
/// <c>DbSet</c> on <c>IAppDbContext</c>, DPIA C-D4).
///
/// <para>
/// <b>Beside the criterion query, never merged into it (CTO F1, binding).</b> The two queries
/// have OPPOSITE absent-axis semantics: there an empty axis is corruption and throws; here an
/// absent axis means the clause is OMITTED from the WHERE — never bound as an empty array,
/// because <c>sni_codes &amp;&amp; '{}'</c> and <c>= ANY('{}')</c> are FALSE and would silently
/// return zero rows (the #805-3 shape: not slow, WRONG). The short idioms (positive
/// <c>status = @status</c> polarity, <c>text[]</c> binding via <c>.ToArray()</c>, the explicit
/// command timeout) are deliberately COPIED from the sibling, not shared — the two files change
/// for different reasons (Hunt/Thomas: DRY is one source per knowledge piece, and these clauses
/// encode different knowledge).
/// </para>
///
/// <para>
/// <b>The name axis is a case-insensitive LITERAL prefix</b> (CTO F2):
/// <c>lower(company_name) LIKE lower(@name_prefix) ESCAPE '\'</c>, where the parameter is the
/// user's term with LIKE metacharacters escaped and <c>%</c> appended. Lower-casing happens on
/// BOTH sides in Postgres (one case-folding authority — the column's ICU <c>swedish</c>
/// collation — never a second fold in C#, which diverges on edge code points). The shape is the
/// only one <c>ix_company_register_company_name_lower</c> (functional btree,
/// <c>text_pattern_ops</c>) can serve, and <c>CompanyRegisterSearchQueryPlanTests</c> pins that
/// index BY NAME — the naive <c>company_name ILIKE</c> form returns the same rows with no index
/// at all, which is exactly the vacuous-guarantee class the pin exists for.
/// </para>
///
/// <para>
/// <b>Known mine, inherited from the sibling (documented, not fixed here):</b> with
/// <c>Max Auto Prepare</c> enabled the statement goes generic, and a generic LIKE plan cannot
/// derive the prefix range from an unknown parameter — the pattern index falls out of the plan.
/// Today Npgsql sends UNNAMED statements (custom plans, actual values), so the prefix range IS
/// derived. The sibling's <c>GenericPlan_DoesNotUseTheNameIndex_SoMaxAutoPrepareWouldKillIt</c>
/// documents the same class for the ORDER BY index; re-measure BOTH before enabling
/// Max Auto Prepare (docs/PERFORMANCE_AUDIT.md).
/// </para>
/// </summary>
internal sealed class CompanyRegisterSearchQuery(AppDbContext db) : ICompanyRegisterSearchQuery
{
    /// <summary>
    /// Explicit, reviewed — never inherited (the sibling's discipline, security-auditor Minor
    /// 2026-07-13): a raw <see cref="NpgsqlCommand"/> does NOT pick up EF's
    /// <c>SetCommandTimeout</c>. Same ceiling-on-a-bug argument as
    /// <see cref="CompanyWatchBrowseQuery.CommandTimeoutSeconds"/> — a browse that takes 30 s is
    /// a bug, and the timeout stops it from starving the Npgsql pool.
    /// </summary>
    internal const int CommandTimeoutSeconds = 30;

    private const string SelectColumns =
        "SELECT organization_number, company_name, sate_kommun_code, sate_kommun_name, sni_codes ";

    public async ValueTask<PagedResult<CompanyBrowseResult>> SearchAsync(
        CompanyRegisterSearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Separate count query BEFORE pagination (CLAUDE.md §3.6). Same composed WHERE, same
        // bound values — BuildCountCommand and BuildItemsCommand share ComposeFromWhere and
        // BindPredicate, so the two cannot drift.
        int totalCount;
        await using (var countCmd = BuildCountCommand(connection, criteria))
        {
            var scalar = await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            totalCount = Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
        }

        var items = new List<CompanyBrowseResult>();
        await using (var itemsCmd = BuildItemsCommand(connection, criteria))
        {
            await using var reader = await itemsCmd
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new CompanyBrowseResult(
                    OrganizationNumber: reader.GetString(0),
                    Name: reader.GetString(1),
                    SeatMunicipalityCode: reader.GetString(2),
                    SeatMunicipalityName: await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                        ? null
                        : reader.GetString(3),
                    SniCodes: reader.GetFieldValue<string[]>(4)));
            }
        }

        return new PagedResult<CompanyBrowseResult>(
            items, totalCount, criteria.Page, criteria.PageSize);
    }

    /// <summary>
    /// The magnitude count: <c>min(true count, ceiling)</c> over the SAME composed predicate as
    /// the page query (one predicate authority — the sibling's Fork G3 bind, applied here). Same
    /// statement as the pagination count; only the bound cap differs.
    /// </summary>
    public async ValueTask<int> CountMatchingAsync(
        CompanyRegisterSearchCriteria criteria, int ceiling, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceiling, 1);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = BuildMagnitudeCommand(connection, criteria, ceiling);
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The page query, exactly as production emits it — <c>internal</c> so
    /// <c>CompanyRegisterSearchQueryPlanTests</c> can EXPLAIN THIS command rather than a
    /// hand-typed lookalike (the sibling's oracle discipline: a re-typed query is not an oracle,
    /// and the factories carry the parameter TYPES too).
    /// </summary>
    internal static NpgsqlCommand BuildItemsCommand(
        NpgsqlConnection connection, CompanyRegisterSearchCriteria criteria)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText =
            SelectColumns
            + ComposeFromWhere(criteria)
            // ORDER BY is TOTAL (duplicate legal names are normal; Postgres sorts are not
            // stable): organization_number is the PK tiebreak. NO explicit COLLATE — the column
            // carries `swedish` (ICU sv-SE, #884) and the ORDER BY index was built under it; a
            // written COLLATE would stop the inheritance and silently Sort the whole match set
            // (see the sibling plan test's BrokenPlanMessage for the full trap).
            + """

              ORDER BY company_name, organization_number
              LIMIT @limit OFFSET @offset;
              """;
        BindPredicate(cmd, criteria);
        cmd.Parameters.AddWithValue("@limit", NpgsqlDbType.Integer, criteria.PageSize);
        cmd.Parameters.AddWithValue(
            "@offset", NpgsqlDbType.Integer, (criteria.Page - 1) * criteria.PageSize);
        return cmd;
    }

    /// <summary>
    /// The pagination count, capped at <see cref="CompanyRegisterSearchCriteria.MaxServableRows"/>
    /// — a CORRECTNESS cap (<c>TotalPages ≤ MaxPage</c> by construction; the sibling's CountSql
    /// doc carries the full argument, and it bites HARDER here: browse-all matches all ~1,07M
    /// rows). The subquery selects a constant; the cap costs only the LIMIT.
    /// </summary>
    internal static NpgsqlCommand BuildCountCommand(
        NpgsqlConnection connection, CompanyRegisterSearchCriteria criteria)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText =
            "SELECT count(*) FROM (SELECT 1 " + ComposeFromWhere(criteria) + " LIMIT @count_cap) t;";
        BindPredicate(cmd, criteria);
        // Derived from the page cap, never a hand-picked constant (one knowledge piece).
        cmd.Parameters.AddWithValue(
            "@count_cap",
            NpgsqlDbType.Integer,
            CompanyRegisterSearchCriteria.MaxServableRows(criteria.PageSize));
        return cmd;
    }

    /// <summary>
    /// The magnitude query — SAME statement as <see cref="BuildCountCommand"/>, only the cap is
    /// the caller's PRODUCT ceiling.
    /// </summary>
    internal static NpgsqlCommand BuildMagnitudeCommand(
        NpgsqlConnection connection, CompanyRegisterSearchCriteria criteria, int ceiling)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText =
            "SELECT count(*) FROM (SELECT 1 " + ComposeFromWhere(criteria) + " LIMIT @count_cap) t;";
        BindPredicate(cmd, criteria);
        cmd.Parameters.AddWithValue("@count_cap", NpgsqlDbType.Integer, ceiling);
        return cmd;
    }

    /// <summary>
    /// The predicate — composed in ONE place for all three commands, so count, page and
    /// magnitude can never drift (the sibling single-sources a <c>const</c>; here the WHERE is
    /// conditional, so the single source is this method + <see cref="BindPredicate"/> as a
    /// pair). An ABSENT axis contributes NO clause — the anti-silent-zero rule this port exists
    /// for (interface doc).
    /// </summary>
    private static string ComposeFromWhere(CompanyRegisterSearchCriteria criteria)
    {
        // Positive polarity (`status = @status`), the sibling's #805-3 defense: the day the
        // status enum gains a third member, a negative form would silently start SURFACING it.
        var sql = new StringBuilder(
            """
            FROM company_register
            WHERE status = @status
            """);

        if (criteria.MunicipalityCodes.Count > 0)
            sql.Append("\n  AND sate_kommun_code = ANY(@kommun)");

        if (criteria.SniCodes.Count > 0)
            sql.Append("\n  AND sni_codes && @sni");

        if (criteria.OrganizationNumber is not null)
            sql.Append("\n  AND organization_number = @orgnr");

        if (criteria.NamePrefix is not null)
        {
            // lower() on BOTH sides — the indexed expression on the left, the parameter on the
            // right (one case-folding authority: Postgres/ICU). ESCAPE '\' is explicit so the
            // escaping BindPredicate applies is the escaping this clause reads.
            sql.Append("\n  AND lower(company_name) LIKE lower(@name_prefix) ESCAPE '\\'");
        }

        return sql.ToString();
    }

    /// <summary>
    /// Binds exactly the parameters <see cref="ComposeFromWhere"/> emitted for this criteria —
    /// text AND values single-sourced as a pair (a count binding different values than the page
    /// reports a silently wrong total with an identical predicate).
    /// </summary>
    private static void BindPredicate(NpgsqlCommand cmd, CompanyRegisterSearchCriteria criteria)
    {
        // nameof, not a literal (§5 magic strings): the status column is persisted BY NAME.
        cmd.Parameters.AddWithValue(
            "@status", NpgsqlDbType.Text, nameof(CompanyRegisterStatus.Active));

        // text[] parameters, the ScbCompanyRegisterStore idiom (.ToArray() — IReadOnlyList does
        // not bind reliably to text[]). ONLY bound when the clause was emitted: binding an empty
        // array would be harmless here (the clause is absent), but the discipline keeps the
        // parameter list an exact mirror of the WHERE.
        if (criteria.MunicipalityCodes.Count > 0)
        {
            cmd.Parameters.Add(new NpgsqlParameter("@kommun", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = criteria.MunicipalityCodes.ToArray(),
            });
        }

        if (criteria.SniCodes.Count > 0)
        {
            cmd.Parameters.Add(new NpgsqlParameter("@sni", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = criteria.SniCodes.ToArray(),
            });
        }

        if (criteria.OrganizationNumber is not null)
            cmd.Parameters.AddWithValue("@orgnr", NpgsqlDbType.Text, criteria.OrganizationNumber);

        if (criteria.NamePrefix is not null)
        {
            cmd.Parameters.AddWithValue(
                "@name_prefix", NpgsqlDbType.Text, EscapeLikePrefix(criteria.NamePrefix));
        }
    }

    /// <summary>
    /// The user's term is LITERAL (VO doc): LIKE's metacharacters are data, so they are escaped
    /// before the single trailing <c>%</c> that makes it a prefix. Backslash first — escaping
    /// the escape character last would re-escape the escapes.
    /// </summary>
    internal static string EscapeLikePrefix(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
        + "%";

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
