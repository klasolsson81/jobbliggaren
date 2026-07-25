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

        // The page query's PLAN depends on whether the match set is bounded, and the count above is
        // exactly that signal (see BuildItemsCommand). Measure first, then plan — no extra round-trip.
        var items = new List<CompanyBrowseResult>();
        await using (var itemsCmd = BuildItemsCommand(connection, criteria, totalCount))
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
    ///
    /// <para>
    /// <b>Two plan regimes, one rule (ADR 0119).</b> <paramref name="matchCount"/> is the
    /// pagination count <see cref="SearchAsync"/> has already run, and it SATURATES at
    /// <see cref="CompanyRegisterSearchCriteria.MaxServableRows"/> — so a count below the cap is a
    /// GUARANTEE (not an estimate) that the whole match set fits inside what the pager can serve.
    /// The match set is materialized iff we can bound it:
    /// <c>NamePrefix is not null OR matchCount &lt; MaxServableRows(pageSize)</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Why.</b> Unmaterialized, the planner answers <c>LIMIT 20</c> by walking
    /// <c>ix_company_register_company_name_organization_number</c> in sort order and hoping to stop
    /// early. Whether that gamble is safe depends on the predicate's relationship to the SORT KEY,
    /// and there are exactly two cases. Let <i>depth</i> be how far into the ordered index the 20th
    /// match sits — the walk's real cost.
    /// </para>
    ///
    /// <para>
    /// <b>Klustrad sist</b> (the name prefix). The predicate constrains <c>company_name</c>, which
    /// IS the sort key, so every match sits in ONE contiguous run and depth is set by where that run
    /// falls in the alphabet — <b>decoupled from the match count entirely</b>. Nothing available at
    /// compose time predicts it, so no count threshold can work and materialization is
    /// unconditional. Measured on the 1 066 938-row dev register (743 654 <c>Active</c>,
    /// 2026-07-25, plain query, median of five): <c>s%</c> 1 702 ms, <c>m%</c> 1 022 ms, <c>h%</c>
    /// 671 ms — while <c>a%</c> takes 15 ms at a COMPARABLE match size, purely because 'A' sorts
    /// first. 19 of 29 single-character prefixes breached ADR 0045's 300 ms class (a), worst
    /// <c>w%</c> 2 141 ms.
    /// </para>
    ///
    /// <para>
    /// <b>Gles och jämnt utspridd</b> (kommun, SNI, org.nr). The predicate is independent of the
    /// sort key, so matches are scattered evenly and <c>depth ≈ N_active × pageSize / matches</c> —
    /// the planner's uniformity model is CORRECT here (predicted 97 200 for kommun 2403, measured
    /// 104 322). Depth is therefore knowable, and it is INVERSELY proportional to the match count
    /// while materialization cost is DIRECTLY proportional to it. The two cross over, so the count
    /// is the sufficient statistic — and only here. Measured: kommun 2403 (153 Active)
    /// <b>3 966 ms</b> → 25 ms, kommun 2521 (441) 506 ms → 1,3 ms.
    /// </para>
    ///
    /// <para>
    /// <b>Neither clause is redundant, and that is checkable.</b> The count clause's marginal
    /// coverage is the sparse axes above. The name clause's is exactly the BROAD prefixes, whose
    /// counts saturate the cap and which the count clause therefore cannot reach. They do not
    /// overlap in what they rescue, so a future reader who wants to delete one can be answered with
    /// a measurement rather than an argument.
    /// </para>
    ///
    /// <para>
    /// <b>Why a rule and not an axis list — the big-kommun counter-evidence.</b> Göteborg (49 639
    /// Active) SATURATES the count and keeps the walk: 9,5 ms, preserved. Stockholm (112 383):
    /// 0,8 ms. Materialized they would cost 50 ms and 96 ms, so an axis list naming "name + kommun"
    /// would have made the two biggest kommuner 5× and 120× worse. Browse-all likewise saturates and
    /// keeps its walk. The rule is not "materialize when in doubt" — it is "materialize when we can
    /// BOUND it", and a saturated count is precisely the signal that we cannot, and need not.
    /// </para>
    ///
    /// <para>
    /// <b>SNI belongs to the sparse-and-spread regime too, and the count clause covers it
    /// PREEMPTIVELY.</b> A narrow SNI (35210, 100 <c>Active</c>) is healthy UNFIXED at 0,4 ms — the
    /// walk was never chosen there, because that axis is GIN-served and its bitmap estimates are
    /// honest, so the planner already prices the right plan right. That is not a counter-example to
    /// the regime: the depth arithmetic is identical, only the planner's INPUT is better. The count
    /// clause materializes it anyway (100 &lt; cap) at no measurable cost, which is what makes this
    /// a rule about axes independent of the sort key rather than a kommun patch.
    /// </para>
    ///
    /// <para>
    /// <b>Accepted regression, stated plainly.</b> A prefix that sorts EARLY gets slower:
    /// <c>a%</c> 15 → 206 ms, <c>b%</c> 155 → 218 ms. That is paid deliberately for a BOUNDED worst
    /// case instead of a fast average with a 2 141 ms tail. Bounded beats lucky: budgets are p95/p99
    /// statements, and predictability is the product property.
    /// </para>
    ///
    /// <para>
    /// <b>The new worst case, measured on the five widest match sets</b> (widest ⇒ they bound the
    /// materialized branch; plain query, median of five): <c>s%</c> 83 267 matches → 264 ms ·
    /// <c>b%</c> 63 052 → 218 ms · <c>a%</c> 56 050 → 206 ms · <c>m%</c> 47 807 → 198 ms ·
    /// <c>h%</c> 41 576 → 200 ms. Worst 264 ms, inside ADR 0045's 300 ms class (a). Earlier readings
    /// of 312 ms (<c>h%</c>) and 305 ms (<c>s%</c>) that tripped a review gate were
    /// <c>EXPLAIN ANALYZE</c> per-node overhead, not request cost — a budget is a claim about what
    /// the REQUEST costs, so it must be measured in the shape production emits.
    /// </para>
    ///
    /// <para>
    /// <b>Statistics are not an alternative to this rule — they are orthogonal to it.</b>
    /// <c>company_register</c> had NEVER been ANALYZEd (zero rows in <c>pg_stats</c>); running
    /// ANALYZE alone fixed a SELECTIVE prefix (<c>spotify%</c> 2 084 → 0,153 ms) and made the sparse
    /// kommun case sixteen times WORSE (244 → 3 966 ms), because a better-informed planner commits
    /// HARDER to a walk whose depth it now believes it can predict. And the planner cannot be
    /// argued out of it: for <c>w%</c> it prices the walk at 398 and the correct materialized plan
    /// at 19 842 — 50× more expensive — so no statistics fix and no additional index moves it.
    /// (A single index cannot serve both halves anyway: the prefix filter needs
    /// <c>text_pattern_ops</c> byte order and the ordering needs <c>swedish</c> ICU — #884/ADR 0110.)
    /// </para>
    ///
    /// <para>
    /// <b>Both branches are semantics-identical.</b> The CTE carries NO <c>ORDER BY</c> and NO
    /// <c>LIMIT</c> — ordering and pagination live in the outer query only, so both branches return
    /// the same rows in the same order and a wrong branch can only ever be a performance bug, never
    /// a correctness one. An inner <c>LIMIT</c> would take an arbitrary subset and then order it:
    /// silently wrong rows, page-dependent, every semantic test green (the #805-3 class).
    /// <c>CompanyRegisterSearchQueryCompositionTests</c> pins that shape structurally (the branch
    /// taken per axis, and ORDER BY/LIMIT/OFFSET occurring exactly once, at the very end), and
    /// <c>CompanyRegisterSearchPlanChoiceTests</c> pins both regimes' PLAN at production
    /// cardinality with no GUC.
    /// </para>
    /// </summary>
    internal static NpgsqlCommand BuildItemsCommand(
        NpgsqlConnection connection, CompanyRegisterSearchCriteria criteria, int matchCount)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;

        // ORDER BY is TOTAL (duplicate legal names are normal; Postgres sorts are not stable):
        // organization_number is the PK tiebreak. NO explicit COLLATE — the column carries `swedish`
        // (ICU sv-SE, #884) and the ORDER BY index was built under it; a written COLLATE would stop
        // the inheritance and silently Sort the whole match set (see the sibling plan test's
        // BrokenPlanMessage for the full trap). It is written ONCE and shared by both branches, so
        // the two can never drift in ordering.
        const string OrderAndPaginate = """

            ORDER BY company_name, organization_number
            LIMIT @limit OFFSET @offset;
            """;

        cmd.CommandText = ShouldMaterialize(criteria, matchCount)
            // The outer SELECT repeats the columns rather than `SELECT *`: the reader below reads BY
            // ORDINAL, and spelling them out keeps that contract identical in both branches.
            ? "WITH m AS MATERIALIZED (" + SelectColumns + ComposeFromWhere(criteria) + ")\n"
              + SelectColumns + "FROM m" + OrderAndPaginate
            : SelectColumns + ComposeFromWhere(criteria) + OrderAndPaginate;

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
    /// THE plan rule (ADR 0119), in one named place so the guard exercises production's decision
    /// rather than re-deriving it: materialize the match set iff we can BOUND it — the predicate
    /// constrains <c>company_name</c>, or the pagination count did not saturate.
    ///
    /// <para>
    /// No new constant and no tuning knob: the ceiling is
    /// <see cref="CompanyRegisterSearchCriteria.MaxServableRows"/>, the SAME knowledge piece
    /// <see cref="BuildCountCommand"/> caps with. A saturated count is precisely the statement
    /// "we do not know how big this match set is" — and an unsaturated one bounds the match set at
    /// <c>100 × pageSize</c>: 2 000 rows at the default page size, 10 000 at the largest
    /// <c>pageSize</c> a caller may request. Only the 2 000-row bound is measured directly; the
    /// 10 000-row one is bounded from above by the big-kommun measurement (49 639 rows materialize
    /// in 50 ms).
    /// </para>
    ///
    /// <para>
    /// <b>The threshold was validated at its own boundary, not extrapolated</b> (ADR 0119): the four
    /// kommuner bracketing the cap (1 880 / 1 976 / 2 019 / 2 037 <c>Active</c>) measured
    /// 11,0–17,2 ms unfixed and 3,7–4,1 ms materialized (plain query, median of five). All four take
    /// the ORDERED WALK unfixed — verified by EXPLAIN, so the boundary was measured on the branch it
    /// is about — and there is no cliff on either side of the switch.
    /// </para>
    ///
    /// <para>
    /// <b>Why those milliseconds look "too cheap" against the depth model above, reconciled.</b>
    /// Depth at the cap is ~10 000 rows (kommun 1487, measured: <c>Rows Removed by Filter</c>
    /// 10 076) — an order of magnitude shallower than the sparse cases — and every buffer was a
    /// cache hit (<c>shared hit=10065</c>, zero reads). Per-row walk cost is therefore NOT a
    /// constant to extrapolate with: ~1,3 µs while the walk fits in <c>shared_buffers</c>, ~38 µs
    /// for the 104 322-row walk that does not. That is why the deep cases are catastrophic and the
    /// boundary is not, and it is also why these boundary figures are a warm-cache floor rather than
    /// a p95 claim. The rule does not rest on them: it switches on the count, and both sides of the
    /// switch were measured in budget.
    /// </para>
    /// </summary>
    private static bool ShouldMaterialize(CompanyRegisterSearchCriteria criteria, int matchCount) =>
        criteria.NamePrefix is not null
        || matchCount < CompanyRegisterSearchCriteria.MaxServableRows(criteria.PageSize);

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
