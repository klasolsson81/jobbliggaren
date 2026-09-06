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
    internal const string FromWhere = """
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
    /// 400. An UNCAPPED count over a bound-legal broad criterion — one matching far more rows than
    /// this surface can serve — would have the pager advertise many times the 100 pages that are
    /// fetchable: an authoritative number the system that emitted it does not back — the #805-3 shape, not slow but
    /// FALSE. The cap makes <c>TotalPages &lt;= MaxPage</c> true by construction.
    ///
    /// <para>
    /// It is also, incidentally, what keeps the count off an exact <c>count(*)</c> over the whole register. That
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
    /// #1559, re-based on the materialised member set by #1681 part 2 (ADR 0139) - the criterion's
    /// ACTIVE-ad predicate. <b>It no longer touches <c>company_register</c> at all.</b>
    ///
    /// <para>
    /// <b>This is the whole point of ADR 0139, expressed as a FROM clause.</b> The predicate's
    /// expensive half - resolving SNI-overlap AND kommun-membership against 1,07M register rows -
    /// was moved out of the request path into a recurring job, so what is left here is an index join
    /// against a pre-computed, breadth-gated org.nr set. Measured: the plan contains no
    /// <c>company_register</c> node, its <c>InitPlan</c> is an Index Only Scan on the member PK with
    /// <c>Heap Fetches: 0</c>, and the outer query is a Bitmap Index Scan on
    /// <c>ix_job_ads_organization_number</c>
    /// (<c>docs/reviews/2026-09-06-1681-part2-read-form-measurement.md</c>, Result 3).
    /// </para>
    ///
    /// <para>
    /// <b><c>= ANY(ARRAY(subselect))</c>, not a JOIN against the member table, and that is a MEASURED
    /// choice rather than a stylistic one</b> (senior-cto-advisor 2026-09-06). The two forms cross:
    /// this one keeps the twin handler's exact plan and its cost grows with the criterion's ad set,
    /// while a JOIN is driven by <c>job_ads</c> and costs the same whatever the member count - which
    /// makes it flat but strictly worse at every size an ordinary criterion has. It is also the shape
    /// the breadth gate's bound was DERIVED against, so this statement inherits that derivation
    /// instead of owing a new one. <b>Do not "simplify" it into a JOIN.</b>
    /// </para>
    ///
    /// <para>
    /// <b><c>j.status = @ad_status</c> is the WHOLE ad-side exclusion.</b> <c>JobAd</c> has no
    /// soft-delete axis and no query filter (#821) - a retracted ad is excluded by its Status, and
    /// there is no <c>deleted_at</c> predicate to add here (ADR 0048 forbids a hand-rolled one).
    /// </para>
    ///
    /// <para>
    /// <b>The set cannot double-count.</b> <c>company_watch_criterion_members</c> has PK
    /// <c>(criterion_id, organization_number)</c>, so a criterion names each org.nr at most once and
    /// <c>= ANY</c> over that set matches each ad exactly once. The register's array-overlap predicate
    /// USED to carry this risk (one company matching several of the criterion's SNI codes); the
    /// materialisation collapsed it to a set, so the hazard is now absent by construction rather than
    /// avoided by the PK of a table this statement no longer reads.
    /// </para>
    /// </summary>
    private const string MaterialisedAdsFromWhere = """
        FROM job_ads j
        WHERE j.organization_number = ANY(ARRAY(
                SELECT organization_number
                FROM company_watch_criterion_members
                WHERE criterion_id = @criterion_id))
          AND j.status = @ad_status
        """;

    // ORDER BY is TOTAL for the same reason ItemsSql's is: published_at is not unique (a bulk
    // ingest stamps many ads identically), Postgres sorts are not stable, and a non-total order
    // plus OFFSET can drop or duplicate rows ACROSS pages. j.id is the PK - it makes the order
    // total. The port's doc publishes this order because the CALLER re-orders by it.
    //
    // SHARED by the page query and the whole-set query, and that sharing is load-bearing (#1656 (b)):
    // the filtered view paginates the SET while the unfiltered view paginates the PAGE query, so two
    // different orders here would sequence one against the other.
    private const string MaterialisedAdsOrderBy = """

        ORDER BY j.published_at DESC, j.id
        """;

    /// <summary>
    /// The state gate every materialised read is wrapped in. Driving the statement FROM
    /// <c>company_watch_criterion_materialisations</c> - rather than reading the ads and asking about
    /// the state afterwards - is what makes the three honest answers come back in ONE round trip:
    /// no row at all is "never materialised", a row whose state or fingerprint disqualifies it yields
    /// no ad work at all, and only a row that passes both reaches the join.
    ///
    /// <para>
    /// <b>The fingerprint comparison is IN the statement, not in C#</b>, so a mismatched predicate
    /// costs nothing: Postgres evaluates it as a one-time filter and skips the ad scan entirely
    /// (verified in the plan as <c>One-Time Filter</c>). Doing it in C# would have read the ads first
    /// and thrown them away.
    /// </para>
    /// </summary>
    private const string MaterialisedGate =
        "m.state = @materialised_state AND m.criteria_fingerprint = @fingerprint";

    /// <summary>
    /// The capped ad count, wrapped in the state gate. <c>CASE</c> rather than a <c>WHERE</c> on the
    /// outer row, because the outer row must come back even when the gate fails - its
    /// <c>state</c> is the answer in that case, and filtering it away would make "too broad" and
    /// "never materialised" indistinguishable (both would be zero rows).
    /// </summary>
    private const string MaterialisedAdCountSql =
        "SELECT m.state, m.criteria_fingerprint, CASE WHEN "
        + MaterialisedGate
        + " THEN (SELECT count(*) FROM (SELECT 1 "
        + MaterialisedAdsFromWhere
        + """
         LIMIT @count_cap) t) END AS ads
        FROM company_watch_criterion_materialisations m
        WHERE m.criterion_id = @criterion_id;
        """;

    /// <summary>
    /// The whole ordered ad-id set, wrapped in the state gate via a LEFT JOIN LATERAL so the state row
    /// survives an empty (or gated-away) ad set.
    ///
    /// <para>
    /// <b>The lateral was measured, not assumed, because a lateral is exactly where this codebase has
    /// been burned before.</b> ADR 0139 rejected a batched form whose lateral sat over a
    /// <c>jsonb_to_recordset</c> function scan - the planner had no statistics for it and stopped
    /// choosing the index lookup, costing 40-50x. This lateral is over a REAL table with a correlated
    /// equality on its PK, and the plan is unchanged from the un-wrapped statement: same Index Only
    /// Scan (<c>Heap Fetches: 0</c>), same Bitmap Index Scan, 44 buffers against 43. The gate appears
    /// as a <c>One-Time Filter</c>.
    /// </para>
    ///
    /// <para>
    /// <b>The outer ORDER BY is not redundant.</b> The lateral's own ORDER BY is what the LIMIT cuts
    /// against, so it selects the right rows; but the published order is part of the port's CONTRACT,
    /// and relying on a Nested Loop to preserve the inner order is relying on a plan shape rather than
    /// on the statement. It re-sorts at most <c>maxSetSize + 1</c> rows.
    /// </para>
    /// </summary>
    private const string MaterialisedAdIdSetSql =
        """
        SELECT m.state, m.criteria_fingerprint, a.id
        FROM company_watch_criterion_materialisations m
        LEFT JOIN LATERAL (
        """
        // A raw string literal does NOT keep the newline before its closing delimiter, so a fragment
        // ending in a column name would butt straight up against MaterialisedAdsFromWhere's leading
        // `FROM` and emit `j.published_atFROM job_ads j`. That is invisible when reading either
        // literal, and it is why every fragment on this seam ends in an ordinary quoted string with a
        // trailing space — the shape the sibling statements already use.
        + "SELECT j.id, j.published_at "
        + MaterialisedAdsFromWhere
        + " AND "
        + MaterialisedGate
        + MaterialisedAdsOrderBy
        + """
         LIMIT @set_limit) a ON true
        WHERE m.criterion_id = @criterion_id
        ORDER BY a.published_at DESC, a.id;
        """;

    /// <summary>
    /// One PAGE of ad ids. Unlike its two siblings this statement carries no state gate, and
    /// deliberately: <see cref="BrowseAdIdsAsync"/> reads the state via
    /// <see cref="MaterialisedAdCountSql"/> first (it needs the capped total anyway) and only issues
    /// this one once the gate has passed - so the gate is paid for once rather than twice, and this
    /// statement's plan stays byte-for-byte the shape the measurement covers.
    /// </summary>
    private const string MaterialisedAdIdsSql =
        "SELECT j.id "
        + MaterialisedAdsFromWhere
        + MaterialisedAdsOrderBy
        + """

        LIMIT @limit OFFSET @offset;
        """;

    public async ValueTask<MaterialisedAdPage> BrowseAdIdsAsync(
        CompanyWatchCriterionId criterionId, CriteriaFingerprint fingerprint,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Separate count query BEFORE pagination (CLAUDE.md 3.6) - and here it doubles as the state
        // read, so the gate costs no extra round trip.
        var counted = await ReadAdCountAsync(
            connection, criterionId, fingerprint,
            CompanyBrowseCriteria.MaxServableRows(pageSize), cancellationToken).ConfigureAwait(false);

        if (counted.State != CriterionMaterialisationState.Materialised)
        {
            return counted.State == CriterionMaterialisationState.TooBroad
                ? MaterialisedAdPage.TooBroad
                : MaterialisedAdPage.NotMaterialised;
        }

        var ids = new List<JobAdId>();
        await using (var idsCmd = BuildAdIdsCommand(connection, criterionId, page, pageSize))
        {
            await using var reader = await idsCmd
                .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                ids.Add(new JobAdId(reader.GetGuid(0)));
        }

        return MaterialisedAdPage.Resolved(
            new PagedResult<JobAdId>(ids, counted.Count!.Value, page, pageSize));
    }

    public async ValueTask<MaterialisedAdCount> CountActiveAdsAsync(
        CompanyWatchCriterionId criterionId, CriteriaFingerprint fingerprint, int ceiling,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ceiling, 1);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAdCountAsync(connection, criterionId, fingerprint, ceiling, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<MaterialisedAdCount> ReadAdCountAsync(
        NpgsqlConnection connection,
        CompanyWatchCriterionId criterionId,
        CriteriaFingerprint fingerprint,
        int cap,
        CancellationToken cancellationToken)
    {
        await using var cmd = BuildAdCountCommand(connection, criterionId, fingerprint, cap);
        await using var reader = await cmd
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        // No row = no materialisation has ever run for this criterion. It is NOT a zero, and the
        // difference is the whole reason part 1 wrote a state table at all.
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return MaterialisedAdCount.NotMaterialised;

        var state = reader.GetString(0);
        var storedFingerprint = reader.GetString(1);

        if (state == MaterialisationState.TooBroad.ToString())
            return MaterialisedAdCount.TooBroad;

        // A row whose fingerprint does not match was computed from a predicate the user has since
        // edited. Its member set is exact for a criterion that no longer exists, so the honest answer
        // is that we do not know yet - never the old number, which would be false rather than stale.
        if (!string.Equals(storedFingerprint, fingerprint.Value, StringComparison.Ordinal))
            return MaterialisedAdCount.NotMaterialised;

        // The gate passed, so the CASE produced a number. A NULL here would mean the SQL gate and the
        // C# gate disagree, which is a bug rather than a state - fail loud instead of inventing a 0.
        if (await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Materialiserad rad utan annonstal: SQL-grinden och C#-grinden ar inte langre samma "
                + "villkor. En 0 har vore ett pahittat tal.");
        }

        var count = reader.GetInt32(2);
        return MaterialisedAdCount.Counted(count, saturated: count >= cap);
    }

    public async ValueTask<MaterialisedAdIds> ListActiveAdIdsAsync(
        CompanyWatchCriterionId criterionId, CriteriaFingerprint fingerprint, int maxSetSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSetSize, 1);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = BuildAdIdSetCommand(connection, criterionId, fingerprint, maxSetSize);
        await using var reader = await cmd
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return MaterialisedAdIds.NotMaterialised;

        var state = reader.GetString(0);
        var storedFingerprint = reader.GetString(1);

        if (state == MaterialisationState.TooBroad.ToString())
            return MaterialisedAdIds.TooBroad;

        if (!string.Equals(storedFingerprint, fingerprint.Value, StringComparison.Ordinal))
            return MaterialisedAdIds.NotMaterialised;

        // The LEFT JOIN always yields one row. A NULL id on it means the lateral matched nothing,
        // which for a gate that PASSED is an honest empty set - not a refusal, and not ignorance.
        var ids = new List<JobAdId>();
        if (await reader.IsDBNullAsync(2, cancellationToken).ConfigureAwait(false))
            return MaterialisedAdIds.Resolved(ids);

        ids.Add(new JobAdId(reader.GetGuid(2)));

        // Read at most maxSetSize rows. The (maxSetSize + 1)-th row is not DATA - it is the signal
        // that the set does not fit, and reaching it abandons the whole answer rather than returning
        // what was read so far. Returning the prefix is precisely the failure this method exists to
        // make impossible.
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (ids.Count == maxSetSize)
                return MaterialisedAdIds.TooManyAds;

            ids.Add(new JobAdId(reader.GetGuid(2)));
        }

        return MaterialisedAdIds.Resolved(ids);
    }

    /// <summary>
    /// The whole-set query, exactly as production emits it (the EXPLAIN pin covers this command too).
    /// <c>@set_limit</c> is bound to <c>maxSetSize + 1</c>: the statement deliberately asks for ONE
    /// row more than the caller can accept, because "there is another row" is the only way a single
    /// round-trip can distinguish a set that fits from one that does not.
    /// </summary>
    internal static NpgsqlCommand BuildAdIdSetCommand(
        NpgsqlConnection connection, CompanyWatchCriterionId criterionId,
        CriteriaFingerprint fingerprint, int maxSetSize)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = MaterialisedAdIdSetSql;
        BindMaterialisedGate(cmd, criterionId, fingerprint);
        cmd.Parameters.AddWithValue("@set_limit", NpgsqlDbType.Integer, maxSetSize + 1);
        return cmd;
    }

    /// <summary>
    /// The ad-page query, exactly as production emits it. <c>internal</c> for the same reason
    /// <see cref="BuildItemsCommand"/> is: the EXPLAIN pin prefixes an EXPLAIN onto THIS command's
    /// text rather than a hand-typed lookalike.
    /// </summary>
    internal static NpgsqlCommand BuildAdIdsCommand(
        NpgsqlConnection connection, CompanyWatchCriterionId criterionId, int page, int pageSize)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = MaterialisedAdIdsSql;
        cmd.Parameters.AddWithValue("@criterion_id", NpgsqlDbType.Uuid, criterionId.Value);
        BindAdStatus(cmd);
        cmd.Parameters.AddWithValue("@limit", NpgsqlDbType.Integer, pageSize);
        cmd.Parameters.AddWithValue("@offset", NpgsqlDbType.Integer, (page - 1) * pageSize);
        return cmd;
    }

    /// <summary>
    /// The ad-count query, exactly as production emits it - serving BOTH the pagination cap and the
    /// magnitude ceiling, which is why the cap is a parameter here rather than derived inside (the
    /// two callers hold two different product answers to "how far do we count").
    /// </summary>
    internal static NpgsqlCommand BuildAdCountCommand(
        NpgsqlConnection connection, CompanyWatchCriterionId criterionId,
        CriteriaFingerprint fingerprint, int cap)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = MaterialisedAdCountSql;
        BindMaterialisedGate(cmd, criterionId, fingerprint);
        cmd.Parameters.AddWithValue("@count_cap", NpgsqlDbType.Integer, cap);
        return cmd;
    }

    /// <summary>
    /// Binds the criterion, its predicate fingerprint and the two status values every gated
    /// materialised statement shares. ONE routine for both statements, for the reason
    /// <see cref="BindPredicate"/> exists on the register side: a divergence in the bound VALUES is
    /// the half that cannot be seen by reading either statement.
    /// </summary>
    private static void BindMaterialisedGate(
        NpgsqlCommand cmd, CompanyWatchCriterionId criterionId, CriteriaFingerprint fingerprint)
    {
        cmd.Parameters.AddWithValue("@criterion_id", NpgsqlDbType.Uuid, criterionId.Value);
        cmd.Parameters.AddWithValue("@fingerprint", NpgsqlDbType.Text, fingerprint.Value);
        // The enum's own name, never a literal (5 magic strings): the column is written from
        // MaterialisationState.ToString() by CompanyWatchCriterionMemberStore, so the two cannot
        // drift apart without the type itself changing.
        cmd.Parameters.AddWithValue(
            "@materialised_state", NpgsqlDbType.Text, MaterialisationState.Materialised.ToString());
        BindAdStatus(cmd);
    }

    /// <summary>
    /// Binds the ad-side status. Separate from <see cref="BindPredicate"/> because that routine is
    /// shared with the three register-only statements, none of which has an <c>@ad_status</c>
    /// placeholder.
    ///
    /// <para>
    /// The value comes from <c>JobAdStatus.Active</c>, not a literal (5 magic strings):
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
    internal static void BindPredicate(NpgsqlCommand cmd, CompanyWatchCriteriaSpec spec)
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
