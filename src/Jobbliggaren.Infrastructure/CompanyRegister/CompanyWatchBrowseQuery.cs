using System.Data;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #560 kriterie-vågen PR-2 — the <see cref="ICompanyWatchBrowseQuery"/> implementation: paginated
/// array-overlap browse over the local <c>company_register</c> replica. Raw parametrized SQL against
/// the concrete <see cref="AppDbContext"/>, exactly like <see cref="ScbCompanyRegisterStore"/> (the
/// register is Infrastructure-internal — it is NOT a <c>DbSet</c> on <c>IAppDbContext</c>, DPIA C-D4).
///
/// <para>
/// <b>Raw SQL is not a shortcut here — it is the whole point (dotnet-architect Q5).</b> The SNI half
/// of the predicate MUST be emitted as the Postgres array-overlap operator <c>&amp;&amp;</c>, because
/// that is the only shape <c>ix_company_register_sni_codes_gin</c> (GIN, <c>array_ops</c>) can serve.
/// Npgsql does not reliably translate LINQ to <c>&amp;&amp;</c>: the natural
/// <c>.Where(c =&gt; c.SniCodes.Any(s =&gt; userSni.Contains(s)))</c> compiles to an <c>unnest</c>
/// subquery which the GIN index cannot answer — the query still returns the right rows, so every
/// semantic test stays green while the index silently does nothing. PR-1's index would be pure
/// cosmetics. <c>CompanyWatchBrowseQueryPlanTests</c> pins the emitted plan against the GIN index BY
/// NAME, and that pin is mutation-verified against the naive shape.
/// </para>
///
/// <para>
/// <b>Command construction is SPOT'd through <see cref="BuildItemsCommand"/> /
/// <see cref="BuildCountCommand"/>, which the EXPLAIN test calls directly</b> (via the existing
/// <c>InternalsVisibleTo</c>). A test that re-types the SQL by hand is not an oracle — this repo has
/// already shipped exactly that lie: <c>Jobbliggaren.Migrate</c>'s <c>explain-search</c> tool
/// hand-wrote the search SQL, drifted from the production predicate, and (in its own words) "lied in
/// the REASSURING direction". The factories carry the parameter TYPES too, not just the text: binding
/// <c>@sni</c> as <c>text</c> instead of <c>text[]</c> would EXPLAIN a different plan.
/// </para>
/// </summary>
internal sealed class CompanyWatchBrowseQuery(AppDbContext db) : ICompanyWatchBrowseQuery
{
    /// <summary>
    /// Explicit, reviewed — never inherited (security-auditor Minor, 2026-07-13). A raw
    /// <see cref="NpgsqlCommand"/> does NOT pick up EF's <c>SetCommandTimeout</c>; it silently takes the
    /// connection-string default, which is the same trap <see cref="ScbCompanyRegisterStore"/> documents
    /// and sets explicitly around. This port copied that class's connection idiom, so it takes its
    /// timeout discipline too.
    ///
    /// <para>
    /// <b>Re-derived by #875, because the number it used to be justified against is gone.</b> The old
    /// comment read "~10× headroom over the bound-legal worst case ~3,1 s". Two things were wrong with
    /// it the moment #875 landed, and one of them was already wrong: the worst case is now <b>26 ms</b>
    /// (the ORDER BY index turned a full sort of the match set into an ordered walk that stops at
    /// LIMIT 20), so 30 s is ~1 150× headroom, not 10× — and the 3,1 s it cited was itself measured
    /// best-of-3 on a vacuumed table. Production's real pre-index worst case, measured p95 in the
    /// register's post-sync state (which is what a user browses the morning after the nightly SCB sync),
    /// was <b>7 066 ms</b>.
    /// </para>
    ///
    /// <para>
    /// Both numbers are a FULL <see cref="BrowseAsync"/> call — the capped count AND the items query,
    /// which is what the endpoint costs and therefore what ADR 0045's budget governs. They are not the
    /// items query in isolation.
    /// </para>
    ///
    /// <para>
    /// So what is 30 s FOR, now? It is not headroom over a known cost — it is a ceiling on how long ONE
    /// browse may hold a pooled connection when something is wrong that we did not predict: a cold
    /// cache, stale statistics, a plan regression, a register that has grown past what we measured. A
    /// browse that takes 30 s is a bug, and the timeout is what stops that bug from becoming an
    /// app-wide brownout by starving the Npgsql pool. Deliberately not tighter: a spurious 500 on a
    /// bound-legal criterion would be worse than a slow answer.
    /// </para>
    ///
    /// <para>
    /// It remains a backstop, not the fix. The pool-exhaustion surface is bounded properly by the
    /// ORDER BY index (#875, shipped here) plus PR-3's per-user rate limit.
    /// </para>
    /// </summary>
    internal const int CommandTimeoutSeconds = 30;

    /// <summary>
    /// The predicate — single-sourced so the count query and the page query can NEVER drift apart (a
    /// drift here is a silently wrong total, not a crash).
    ///
    /// <para>
    /// <b><c>status = @status</c> is deliberately POSITIVE polarity.</b>
    /// <see cref="ScbCompanyRegisterStore.DeregisterMissingAsync"/> uses the negative form
    /// (<c>status &lt;&gt; 'Deregistered'</c>) — correct THERE (it is the sweep's "not already dead"
    /// guard), but importing it here would be a latent vacuous filter: the day
    /// <see cref="CompanyRegisterStatus"/> gains a third member, the negative form starts silently
    /// SURFACING it. DPIA M-D6 says Active, always. Positive polarity makes that true by construction
    /// rather than by vigilance — the #805-3 failure shape.
    /// </para>
    /// </summary>
    private const string FromWhere = """
        FROM company_register
        WHERE status = @status
          AND sate_kommun_code = ANY(@kommun)
          AND sni_codes && @sni
        """;

    // ORDER BY is TOTAL: company_name is not unique in a real register (duplicate legal names are
    // normal), and Postgres sorts are not stable, so a non-total ORDER BY + OFFSET can drop or
    // duplicate rows ACROSS pages. organization_number is the PK — it makes the order total.
    private const string ItemsSql =
        "SELECT organization_number, company_name, sate_kommun_code, sate_kommun_name, sni_codes "
        + FromWhere
        + """

        ORDER BY company_name, organization_number
        LIMIT @limit OFFSET @offset;
        """;

    /// <summary>
    /// The count is CAPPED at <c>MaxPage * pageSize</c> — and that is a CORRECTNESS requirement, not a
    /// perf tweak (senior-cto-advisor 2026-07-13). <c>PagedResult.TotalPages</c> is
    /// <c>ceil(TotalCount / PageSize)</c> while <c>CompanyBrowseCriteria.MaxPage</c> makes page 101 a
    /// 400. An UNCAPPED count over a bound-legal broad criterion (1000 SNI x 290 kommuner matches all
    /// 1 170 000 rows) would have the pager advertise 58 500 pages of which 100 are fetchable: an
    /// authoritative number the system that emitted it does not back — the #805-3 shape, not slow but
    /// FALSE. The cap makes <c>TotalPages &lt;= MaxPage</c> true by construction.
    ///
    /// <para>
    /// It is also, incidentally, what keeps the count off an exact <c>count(*)</c> over 1,17M rows. That
    /// is a welcome side effect and NOT the reason — a cap justified by latency is a cap someone removes
    /// the day an index lands.
    /// </para>
    ///
    /// <para>
    /// <b>The numbers this paragraph used to cite (3 147 ms exact / ~78 ms capped) have been withdrawn</b>
    /// (code-reviewer, #875). They came from the same best-of-3, vacuumed-table series that #875's own
    /// re-derivation of <see cref="CommandTimeoutSeconds"/> discredits — a PR cannot retract a number in
    /// one docblock and leave it authoritative in the next. The count query's post-sync p95 is UNMEASURED:
    /// 3 147 ms is a FLOOR, not a worst case (the register's post-sync state made the ITEMS query 2,1x
    /// slower, and the count goes through the same GIN path). Repo precedent for saying so out loud:
    /// #824 — "the application count is a FLOOR, and the copy now says so." Measure it before quoting it.
    /// </para>
    ///
    /// <para>
    /// The subquery selects a constant, not the row: nothing is projected, so the cap costs only the
    /// LIMIT. The inner query carries the SAME <see cref="FromWhere"/> and the SAME bindings as the
    /// page query — the count/page SPOT is untouched.
    /// </para>
    /// </summary>
    private const string CountSql =
        "SELECT count(*) FROM (SELECT 1 " + FromWhere + " LIMIT @count_cap) t;";

    public async ValueTask<PagedResult<CompanyBrowseResult>> BrowseAsync(
        CompanyBrowseCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Separate count query BEFORE pagination (CLAUDE.md §3.6). Same FromWhere, same bound values.
        int totalCount;
        await using (var countCmd = BuildCountCommand(connection, criteria.Criteria, criteria.PageSize))
        {
            var scalar = await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            totalCount = Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
        }

        var items = new List<CompanyBrowseResult>();
        await using (var itemsCmd = BuildItemsCommand(
            connection, criteria.Criteria, criteria.Page, criteria.PageSize))
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
    /// The page query, exactly as production emits it. <c>internal</c> so
    /// <c>CompanyWatchBrowseQueryPlanTests</c> can EXPLAIN THIS command rather than a hand-typed
    /// lookalike — the caller prefixes <c>"EXPLAIN "</c> onto <see cref="NpgsqlCommand.CommandText"/>,
    /// so production carries no diagnostic code path of its own.
    /// </summary>
    internal static NpgsqlCommand BuildItemsCommand(
        NpgsqlConnection connection, CompanyWatchCriteriaSpec spec, int page, int pageSize)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = ItemsSql;
        BindPredicate(cmd, spec);
        cmd.Parameters.AddWithValue("@limit", NpgsqlDbType.Integer, pageSize);
        cmd.Parameters.AddWithValue("@offset", NpgsqlDbType.Integer, (page - 1) * pageSize);
        return cmd;
    }

    /// <summary>The count query, exactly as production emits it. See <see cref="BuildItemsCommand"/>.</summary>
    internal static NpgsqlCommand BuildCountCommand(
        NpgsqlConnection connection, CompanyWatchCriteriaSpec spec, int pageSize)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = CountSql;
        BindPredicate(cmd, spec);
        // Derived from the page cap, never a hand-picked constant: the two are ONE knowledge piece
        // ("how many rows can this surface ever serve"), so they are single-sourced.
        cmd.Parameters.AddWithValue(
            "@count_cap", NpgsqlDbType.Integer, CompanyBrowseCriteria.MaxServableRows(pageSize));
        return cmd;
    }

    /// <summary>
    /// Binds the predicate's parameters. Shared by both commands: SPOT'ing the WHERE *text* alone is
    /// only half the guarantee — a count that bound different VALUES than the page would report a
    /// silently wrong total with an identical predicate.
    /// </summary>
    private static void BindPredicate(NpgsqlCommand cmd, CompanyWatchCriteriaSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // A spec rehydrated from a corrupt row can carry an EMPTY axis: CompanyWatchCriteriaSpec.Create
        // forbids it (Fork B1 — SNI AND kommun both required), but FromTrusted (which the aggregate's
        // Criteria getter uses) does not re-validate, by design. In SQL an empty axis is not an error:
        // `sni_codes && '{}'` is FALSE and `= ANY('{}')` is FALSE, so the browse would return ZERO rows
        // and look like an honest "no companies match". A silent miss is this product's cardinal sin —
        // fail loud instead. (Do NOT copy ScbCompanyRegisterStore's "bind an explicit empty text[]"
        // defense: there an empty array correctly degenerates to a no-op; here it is a wrong ANSWER.)
        if (spec.SniCodes.Count == 0 || spec.MunicipalityCodes.Count == 0)
        {
            throw new InvalidOperationException(
                "CompanyWatchCriteriaSpec har en tom axel (SNI eller kommun) — en browse mot en tom "
                + "axel returnerar tyst noll rader i stället för att fela. Kriteriet är korrupt.");
        }

        // nameof, not a 'Active' literal (§5 magic strings) and not .ToString(): the status column is
        // persisted BY NAME (HasConversion<string>()), so nameof is compile-time exact — rename the
        // enum member and the compiler forces the confrontation with the data migration.
        cmd.Parameters.AddWithValue(
            "@status", NpgsqlDbType.Text, nameof(CompanyRegisterStatus.Active));

        // text[] parameters, the ScbCompanyRegisterStore idiom. .ToArray() is deliberate —
        // IReadOnlyList<string> does not bind reliably to text[].
        cmd.Parameters.Add(new NpgsqlParameter("@kommun", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = spec.MunicipalityCodes.ToArray(),
        });
        cmd.Parameters.Add(new NpgsqlParameter("@sni", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = spec.SniCodes.ToArray(),
        });
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
