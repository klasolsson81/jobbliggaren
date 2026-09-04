using System.Data;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
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
    /// The MAGNITUDE count (CTO Fork G3): <c>min(true count, ceiling)</c> over the SAME
    /// <see cref="FromWhere"/> + <see cref="BindPredicate"/> as the page query — the whole reason
    /// the method lives on this port (predicate drift defense; see the interface doc). The SQL text
    /// is <see cref="CountSql"/> itself: the pagination count and the magnitude count are the same
    /// QUESTION at different ceilings, so they share one statement and differ only in the bound
    /// <c>@count_cap</c>.
    /// </summary>
    public async ValueTask<int> CountMatchingCompaniesAsync(
        CompanyWatchCriteriaSpec criteria, int ceiling, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceiling, 1);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = BuildMagnitudeCommand(connection, criteria, ceiling);
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// #1559 — the criterion's ACTIVE-ad predicate: the SAME register half as <see cref="FromWhere"/>,
    /// joined to <c>job_ads</c> on the org.nr, plus the ad's own Active gate.
    ///
    /// <para>
    /// <b>It is a SEPARATE constant, not a reuse of <see cref="FromWhere"/> with a JOIN prefixed.</b>
    /// The two differ in their FROM clause, so no textual composition could produce both without a
    /// placeholder — and a WHERE text that reads correctly only after substitution is the shape that
    /// makes the EXPLAIN pin stop covering production. The register predicate is instead kept in step
    /// by <see cref="BindPredicate"/>, which binds BOTH families from one routine: a divergence in the
    /// bound VALUES is the failure the count/page SPOT was built against, and it is the half that
    /// cannot be seen by reading either statement.
    /// </para>
    ///
    /// <para>
    /// <b><c>j.status = @ad_status</c> is the WHOLE ad-side exclusion.</b> <c>JobAd</c> has no
    /// soft-delete axis and no query filter (#821) — a retracted ad is excluded by its Status, and
    /// there is no <c>deleted_at</c> predicate to add here (ADR 0048 forbids a hand-rolled one). The
    /// register side keeps its own positive-polarity <c>status = @status</c> for the reason
    /// <see cref="FromWhere"/> gives.
    /// </para>
    ///
    /// <para>
    /// <b>The join key cannot double-count.</b> <c>company_register.organization_number</c> is the
    /// table's PRIMARY KEY, so a given ad joins at most one register row and an ad is counted once
    /// however many SNI codes of the criterion its company carries. Were it not the PK, the
    /// array-overlap predicate would fan out and every number on this surface would be inflated.
    /// </para>
    /// </summary>
    private const string AdsFromWhere = """
        FROM job_ads j
        JOIN company_register c ON c.organization_number = j.organization_number
        WHERE c.status = @status
          AND c.sate_kommun_code = ANY(@kommun)
          AND c.sni_codes && @sni
          AND j.status = @ad_status
        """;

    // ORDER BY is TOTAL for the same reason ItemsSql's is: published_at is not unique (a bulk
    // ingest stamps many ads identically), Postgres sorts are not stable, and a non-total order
    // plus OFFSET can drop or duplicate rows ACROSS pages. j.id is the PK — it makes the order
    // total. The port's doc publishes this order because the CALLER re-orders by it.
    private const string AdIdsSql =
        "SELECT j.id "
        + AdsFromWhere
        + """

        ORDER BY j.published_at DESC, j.id
        LIMIT @limit OFFSET @offset;
        """;

    /// <summary>
    /// The ad count, capped — the same statement shape as <see cref="CountSql"/> over the ad
    /// predicate, so the pagination cap and the magnitude ceiling are again one statement differing
    /// only in the bound <c>@count_cap</c>.
    /// </summary>
    private const string AdCountSql =
        "SELECT count(*) FROM (SELECT 1 " + AdsFromWhere + " LIMIT @count_cap) t;";

    public async ValueTask<PagedResult<JobAdId>> BrowseAdIdsAsync(
        CompanyBrowseCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Separate count query BEFORE pagination (CLAUDE.md §3.6), same predicate, same bindings.
        int totalCount;
        await using (var countCmd = BuildAdCountCommand(
            connection, criteria.Criteria, CompanyBrowseCriteria.MaxServableRows(criteria.PageSize)))
        {
            var scalar = await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            totalCount = Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
        }

        var ids = new List<JobAdId>();
        await using (var idsCmd = BuildAdIdsCommand(
            connection, criteria.Criteria, criteria.Page, criteria.PageSize))
        {
            await using var reader = await idsCmd
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                ids.Add(new JobAdId(reader.GetGuid(0)));
        }

        return new PagedResult<JobAdId>(ids, totalCount, criteria.Page, criteria.PageSize);
    }

    public async ValueTask<int> CountActiveAdsAsync(
        CompanyWatchCriteriaSpec criteria, int ceiling, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceiling, 1);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = BuildAdCountCommand(connection, criteria, ceiling);
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(scalar, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The ad-page query, exactly as production emits it. <c>internal</c> for the same reason
    /// <see cref="BuildItemsCommand"/> is: the EXPLAIN pin prefixes an EXPLAIN onto THIS command's
    /// text rather than a hand-typed lookalike.
    /// </summary>
    internal static NpgsqlCommand BuildAdIdsCommand(
        NpgsqlConnection connection, CompanyWatchCriteriaSpec spec, int page, int pageSize)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = AdIdsSql;
        BindPredicate(cmd, spec);
        BindAdStatus(cmd);
        cmd.Parameters.AddWithValue("@limit", NpgsqlDbType.Integer, pageSize);
        cmd.Parameters.AddWithValue("@offset", NpgsqlDbType.Integer, (page - 1) * pageSize);
        return cmd;
    }

    /// <summary>
    /// The ad-count query, exactly as production emits it — serving BOTH the pagination cap and the
    /// magnitude ceiling, which is why the cap is a parameter here rather than derived inside (the
    /// two callers hold two different product answers to "how far do we count").
    /// </summary>
    internal static NpgsqlCommand BuildAdCountCommand(
        NpgsqlConnection connection, CompanyWatchCriteriaSpec spec, int cap)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = AdCountSql;
        BindPredicate(cmd, spec);
        BindAdStatus(cmd);
        cmd.Parameters.AddWithValue("@count_cap", NpgsqlDbType.Integer, cap);
        return cmd;
    }

    /// <summary>
    /// Binds the ad-side status. Separate from <see cref="BindPredicate"/> because that routine is
    /// shared with the three register-only statements, none of which has an <c>@ad_status</c>
    /// placeholder.
    ///
    /// <para>
    /// The value comes from <c>JobAdStatus.Active</c>, not a literal (§5 magic strings):
    /// <c>job_ads.status</c> is persisted from that SmartEnum's own <c>Value</c>, so the two cannot
    /// drift apart without the type itself changing.
    /// </para>
    /// </summary>
    private static void BindAdStatus(NpgsqlCommand cmd) =>
        cmd.Parameters.AddWithValue("@ad_status", NpgsqlDbType.Text, JobAdStatus.Active.Value);

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
    /// The magnitude query, exactly as production emits it (the EXPLAIN pin covers this command
    /// too). SAME statement as <see cref="BuildCountCommand"/> — only the cap differs: here it is
    /// the caller's PRODUCT ceiling, not the derived pagination cap.
    /// </summary>
    internal static NpgsqlCommand BuildMagnitudeCommand(
        NpgsqlConnection connection, CompanyWatchCriteriaSpec spec, int ceiling)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = CountSql;
        BindPredicate(cmd, spec);
        cmd.Parameters.AddWithValue("@count_cap", NpgsqlDbType.Integer, ceiling);
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
