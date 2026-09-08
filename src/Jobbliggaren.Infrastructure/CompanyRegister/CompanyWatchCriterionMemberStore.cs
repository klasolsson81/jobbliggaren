using System.Data;
using System.Text.Json;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1681 (ADR 0139) — persistence for the criterion-membership materialisation. Raw parametrized
/// PostgreSQL against the concrete <see cref="AppDbContext"/>, exactly like
/// <see cref="ScbCompanyRegisterStore"/> and <see cref="CompanyWatchBrowseQuery"/>: neither
/// <c>company_register</c> nor these two tables is a <c>DbSet</c> on <c>IAppDbContext</c> (DPIA C-D4).
/// The EF configurations exist for the migration schema, not for a read path.
/// </summary>
internal sealed class CompanyWatchCriterionMemberStore(AppDbContext db)
{
    /// <summary>
    /// Explicit, reviewed — never inherited. A raw <see cref="NpgsqlCommand"/> does NOT pick up EF's
    /// <c>SetCommandTimeout</c>; it silently takes the connection-string default, the trap
    /// <see cref="ScbCompanyRegisterStore"/> documents and sets explicitly around.
    ///
    /// <para>
    /// 120 s, matching <see cref="ScbCompanyRegisterStore.CommandTimeoutSeconds"/> rather than the
    /// browse's 30 s, and the difference is the point: the browse's ceiling is sized for an
    /// INTERACTIVE request where a spurious 500 is worse than a slow answer, while these commands run
    /// in a background job where nothing is waiting and the only question is how long a genuinely
    /// hung statement may hold a pooled connection. Measured against it, the margin is large: a full
    /// replace at the bound is tens of milliseconds, so 120 s is three orders of magnitude above the
    /// measured worst case. The figures live in the dated reports and never here — the 1 000-member
    /// replace in <c>docs/reviews/2026-09-06-1681-membership-measurement.md</c>, the 2 500-member one
    /// in <c>docs/reviews/2026-09-08-1706-bound-rederivation.md</c> (#1706). ⚠ The ratio is stated as
    /// an order of magnitude rather than a factor deliberately: it is a quotient of two measurements
    /// taken on different hosts, and a three-significant-figure ratio over that would be false
    /// precision. It is a backstop against the unpredicted — a cold cache, stale statistics,
    /// a plan regression — not headroom over a known cost. Never 0/infinite: a hung command must still
    /// fail loud.
    /// </para>
    /// </summary>
    internal const int CommandTimeoutSeconds = 120;

    private static readonly JsonSerializerOptions BatchJson = new();

    /// <summary>
    /// #1681 clause (ii) — the criteria whose materialisation may be out of date, newest edit first,
    /// capped, each carrying the fingerprint its last materialisation was computed from. The
    /// reconciling sweep's candidate query.
    ///
    /// <para>
    /// <b><c>updated_at</c> is a SUPERSET prefilter here, and deliberately NOT the test</b>
    /// (senior-cto-advisor, 2026-09-07). <c>CompanyWatchCriterion.Rename</c> bumps <c>UpdatedAt</c>
    /// exactly as <c>UpdateCriteria</c> does, so the column is a row mtime rather than a
    /// predicate-change signal — which is the very reason
    /// <see cref="CriteriaFingerprint"/> exists. The asymmetry is the whole design: over-selecting
    /// costs a wasted row read, under-selecting would miss a real edit, so the cheap column selects
    /// and <see cref="CriteriaFingerprint"/> decides. A rename therefore costs one row read and one
    /// SHA-256 and NEVER a register resolution, which is ADR 0139's bound upheld verbatim.
    /// </para>
    ///
    /// <para>
    /// <b>ORDER BY.</b> Criteria with no state row at all come first — someone created a watch and is
    /// waiting for it — and among the rest the newest edit comes first.
    /// </para>
    ///
    /// <para>
    /// ⚠ A candidate the fingerprint dismisses writes nothing, so it is selected again on every tick
    /// until the nightly run re-stamps it. Its worst case is that run's ≤24 h. Issue #1701 carries the
    /// change-reason that removes it.
    /// </para>
    ///
    /// <para>
    /// <b>No OFFSET, and that is not an oversight.</b> A criterion this returns and RESOLVES drops out
    /// of the predicate, so an advancing offset would skip rows — the exact inverse of
    /// <c>CompanyWatchCriterionMaterialiser</c>'s nightly walk, whose OFFSET loop is safe precisely
    /// because its predicate is not mutated by its own work. Copying that loop here would be the bug.
    /// Each tick takes one capped list and the next tick re-derives.
    /// </para>
    ///
    /// <para>
    /// <b>Over-age rows are deliberately NOT selected.</b> The read path's third
    /// <c>NotMaterialised</c> trigger (<see cref="CompanyWatchMaterialisationOptions.MaxReadAgeHours"/>)
    /// stays the nightly run's business: sweeping it would retry a permanently broken criterion every
    /// tick forever, with no new information between attempts.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<StaleCriterion>> SelectStaleCriterionIdsAsync(
        int maxCriteria, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCriteria, 1);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = StaleCriterionIdsSql;
        cmd.Parameters.AddWithValue("@max_criteria", NpgsqlDbType.Integer, maxCriteria);

        await using var reader = await cmd
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var candidates = new List<StaleCriterion>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(new StaleCriterion(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return candidates;
    }

    /// <summary>
    /// A sweep candidate: the criterion, and the fingerprint its LAST materialisation was computed
    /// from (<c>null</c> when it has never been materialised). The stored fingerprint travels back
    /// with the id so the caller can dismiss a rename WITHOUT a second query — the whole point of the
    /// prefilter being a superset is that the exact test must be cheap.
    /// </summary>
    internal readonly record struct StaleCriterion(Guid Id, string? StoredFingerprint);

    private const string StaleCriterionIdsSql =
        """
        SELECT c.id, m.criteria_fingerprint
        FROM company_watch_criteria c
        LEFT JOIN company_watch_criterion_materialisations m ON m.criterion_id = c.id
        WHERE m.criterion_id IS NULL OR m.materialised_at < c.updated_at
        ORDER BY (m.criterion_id IS NOT NULL), c.updated_at DESC
        LIMIT @max_criteria;
        """;

    /// <summary>
    /// The candidate selection: which ACTIVE register companies match this criterion, bounded by the
    /// breadth gate. Returns <c>null</c> when the set is larger than
    /// <paramref name="maxMembers"/> — REFUSED, never truncated.
    ///
    /// <para>
    /// <b>The refusal is STRUCTURAL, not policed</b> — the same mechanism, and the same reasoning, as
    /// <c>ICompanyWatchBrowseQuery.ListActiveAdIdsAsync</c> (senior-cto-advisor 2026-09-05, ADR 0120
    /// clause 5). The statement asks for <c>LIMIT maxMembers + 1</c>, and the existence of that extra
    /// row IS the signal; no code path can return a prefix. A truncated member set would be far worse
    /// than a refused one: every count derived from it would be a FLOOR wearing a magnitude's clothes,
    /// and unlike a saturating count it would have no true reading at all.
    /// </para>
    ///
    /// <para>
    /// <b>No <c>ORDER BY</c>, deliberately</b> — an absence that looks like a bug and is not. The
    /// result is a SET: either it fits, in which case every row comes back and the order is
    /// meaningless, or it does not, in which case only the count matters. Adding an ORDER BY would
    /// force Postgres to materialise and sort the whole match set before the LIMIT could stop it,
    /// which is exactly the 7 066 ms failure #875 was built to remove — and it would do so to
    /// establish an ordering no caller reads.
    /// </para>
    ///
    /// <para>
    /// <b>The predicate is <see cref="CompanyWatchBrowseQuery.FromWhere"/> itself, and the bindings
    /// are its <see cref="CompanyWatchBrowseQuery.BindPredicate"/>.</b> Not a copy: a copy is how the
    /// materialised membership and the live browse come to answer the same question differently, which
    /// is the #1407/#1471 divergence class ADR 0139 exists to close on the OTHER axis (two surfaces,
    /// one source). Sharing the text alone would only be half the guarantee — a statement binding
    /// different VALUES under identical text is the failure the count/page SPOT was built against, and
    /// it is the half you cannot see by reading either statement. It also inherits, for free, the
    /// positive-polarity <c>status = @status</c> whose docblock explains why the negative form would
    /// silently start surfacing a future third <c>CompanyRegisterStatus</c> member, and the fail-loud
    /// empty-axis guard.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<string>?> SelectCandidatesAsync(
        CompanyWatchCriteriaSpec criteria, int maxMembers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMembers, 1);

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = CandidatesSql;
        CompanyWatchBrowseQuery.BindPredicate(cmd, criteria);
        cmd.Parameters.AddWithValue("@member_limit", NpgsqlDbType.Integer, maxMembers + 1);

        await using var reader = await cmd
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var candidates = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // The (maxMembers + 1)-th row is not DATA — it is the proof the set does not fit, and
            // reaching it abandons the whole answer rather than keeping what was read.
            if (candidates.Count == maxMembers)
                return null;

            candidates.Add(reader.GetString(0));
        }

        return candidates;
    }

    private const string CandidatesSql =
        "SELECT organization_number "
        + CompanyWatchBrowseQuery.FromWhere
        + """

        LIMIT @member_limit;
        """;

    /// <summary>
    /// Writes one criterion's conclusion: REPLACES its member set and upserts its state row, in ONE
    /// transaction.
    ///
    /// <para>
    /// <b>Replace, never supplement</b> (security-auditor Major 5c). The <c>DELETE</c> is
    /// unconditional and runs on every path, including the <c>TooBroad</c> path where
    /// <paramref name="organizationNumbers"/> is empty. A criterion the user narrowed — or widened
    /// past the gate — must stop counting the companies it no longer matches: stale members are both
    /// an accuracy defect (Art. 5(1)(d)) and derived personal data kept past its purpose
    /// (Art. 5(1)(e)). Writing the delete as a separate statement rather than folding it into an
    /// upsert is deliberate: an <c>ON CONFLICT</c> upsert would leave orphans behind by construction,
    /// and there is no shape of it that removes a row the new set does not contain.
    /// </para>
    ///
    /// <para>
    /// <b>One transaction, so the two tables cannot disagree.</b> The failure this prevents is not
    /// hypothetical: a crash between the member write and the state write would leave a set of members
    /// alongside a state row saying <c>TooBroad</c> (a count that must not be rendered, next to
    /// material to render it from), or an empty member set marked <c>Materialised</c> with a non-zero
    /// count — an honest-looking zero that is a lie. Both are silent.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing may touch EF's change tracker while this transaction is open</b>
    /// (dotnet-architect, 2026-09-06). The transaction is begun on EF's UNDERLYING connection, so
    /// <c>db.Database.CurrentTransaction</c> is <c>null</c> throughout and EF does not know it exists.
    /// Npgsql auto-enlists commands in an open transaction, so a <c>SaveChangesAsync</c> issued inside
    /// this window would silently join — and roll back with — a transaction EF cannot see, whereas
    /// <c>db.Database.BeginTransactionAsync</c> would at least throw. There is no live hazard today
    /// (the only EF work in the scope is the criterion page read, before the loop, and
    /// <c>EnableRetryOnFailure</c> is not configured so no execution-strategy conflict arises), but
    /// the condition was unwritten, and that is the kind the next person breaks without seeing it.
    /// </para>
    ///
    /// <para>
    /// The insert carries the whole batch as ONE <c>jsonb</c> parameter via
    /// <c>jsonb_to_recordset</c> — the <see cref="ScbCompanyRegisterStore"/> idiom — so a full member
    /// set is one round trip with no 65k-parameter ceiling and no per-row change tracking, whatever
    /// <c>CompanyWatchCriterionMember.MaxPerCriterion</c> is. The measured replace cost per bound is
    /// in the dated reports (#1706 re-measured it at 2 500); it is deliberately not restated here,
    /// because the whole point of the shape is that the round-trip COUNT does not move with the
    /// member count.
    /// </para>
    /// </summary>
    public async Task ReplaceAsync(
        Guid criterionId,
        IReadOnlyList<string> organizationNumbers,
        MaterialisationState state,
        int excludedPersonnummerShaped,
        DateTimeOffset materialisedAt,
        CriteriaFingerprint criteriaFingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizationNumbers);
        if (state == MaterialisationState.TooBroad && organizationNumbers.Count > 0)
        {
            throw new InvalidOperationException(
                "En TooBroad-materialisering får inte bära medlemmar — grinden avstår från att lagra "
                + "mängden, och en lagrad mängd bredvid TooBroad är exakt det tillstånd läsvägen "
                + "aldrig får se.");
        }

        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandTimeout = CommandTimeoutSeconds;
            deleteCmd.CommandText =
                "DELETE FROM company_watch_criterion_members WHERE criterion_id = @criterion_id;";
            deleteCmd.Parameters.AddWithValue("@criterion_id", NpgsqlDbType.Uuid, criterionId);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (organizationNumbers.Count > 0)
        {
            var payload = JsonSerializer.Serialize(
                organizationNumbers.Select(o => new BatchRow(o)), BatchJson);

            await using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandTimeout = CommandTimeoutSeconds;
            insertCmd.CommandText = """
                INSERT INTO company_watch_criterion_members (criterion_id, organization_number)
                SELECT @criterion_id, r.organization_number
                FROM jsonb_to_recordset(@batch::jsonb) AS r(organization_number text);
                """;
            insertCmd.Parameters.AddWithValue("@criterion_id", NpgsqlDbType.Uuid, criterionId);
            insertCmd.Parameters.AddWithValue("@batch", NpgsqlDbType.Jsonb, payload);
            await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var stateCmd = connection.CreateCommand())
        {
            stateCmd.Transaction = transaction;
            stateCmd.CommandTimeout = CommandTimeoutSeconds;
            // state is stored BY NAME (varchar) — .ToString() on the enum, matching the EF
            // HasConversion<string>() the configuration declares.
            stateCmd.CommandText = """
                INSERT INTO company_watch_criterion_materialisations (
                    criterion_id, state, member_count, excluded_personnummer_shaped, materialised_at,
                    criteria_fingerprint)
                VALUES (@criterion_id, @state, @member_count, @excluded_pnr, @materialised_at,
                        @criteria_fingerprint)
                ON CONFLICT (criterion_id) DO UPDATE SET
                    state                       = EXCLUDED.state,
                    member_count                = EXCLUDED.member_count,
                    excluded_personnummer_shaped = EXCLUDED.excluded_personnummer_shaped,
                    materialised_at             = EXCLUDED.materialised_at,
                    criteria_fingerprint        = EXCLUDED.criteria_fingerprint;
                """;
            stateCmd.Parameters.AddWithValue("@criterion_id", NpgsqlDbType.Uuid, criterionId);
            stateCmd.Parameters.AddWithValue("@state", NpgsqlDbType.Text, state.ToString());
            stateCmd.Parameters.AddWithValue(
                "@member_count", NpgsqlDbType.Integer, organizationNumbers.Count);
            stateCmd.Parameters.AddWithValue(
                "@excluded_pnr", NpgsqlDbType.Integer, excludedPersonnummerShaped);
            stateCmd.Parameters.AddWithValue(
                "@materialised_at", NpgsqlDbType.TimestampTz, materialisedAt);
            // Written on EVERY path, TooBroad included: a refusal is about a PREDICATE, and it must
            // stop applying the moment that predicate changes. Without the stamp here, a user who
            // narrowed a too-broad watch would keep being told it is too broad until the next run.
            stateCmd.Parameters.AddWithValue(
                "@criteria_fingerprint", NpgsqlDbType.Text, criteriaFingerprint.Value);
            await stateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes the planner's statistics for BOTH materialisation tables. AGENTS.md §3.6's canonical
    /// argument lives in <see cref="ScbCompanyRegisterStore.AnalyzeAsync"/> and is not restated here.
    ///
    /// <para>
    /// <b>Since #1681 clause (ii) these tables have TWO periodic writers, and are no longer read-only
    /// between nightly runs</b> — the reconciling sweep writes on any minute a user created or edited
    /// a criterion. §3.6's <c>criterion_id</c> condition is unaffected, and the "one writer" condition
    /// still holds in the sense that carries the argument: both writers are the SAME loader going
    /// through the same <c>ReplaceAsync</c>, so there is no second write shape whose statistics could
    /// diverge. What changed is the cadence, which is why the sweep calls this only on a tick that
    /// actually wrote — a tick that loaded nothing is not a bulk-load path.
    /// </para>
    ///
    /// <para>
    /// The specific stake (dotnet-architect, 2026-09-06): the read path's member lookup is served by
    /// an Index Only Scan on the member PK with <c>Heap Fetches: 0</c> — but only while
    /// <c>criterion_id</c>'s selectivity estimate supports it, which is a function of how many
    /// criteria the table holds (#1706). That plan needs current statistics AND a set visibility map,
    /// and the write path is a per-criterion DELETE + INSERT. So this call is what keeps the estimate
    /// honest; it does not by itself decide which plan comes back, and the 2026-09-08 re-derivation
    /// bound the constant under the OTHER plan precisely because correct statistics on a one-user
    /// table produce it.
    /// </para>
    ///
    /// <para>
    /// Called once per COMPLETED run, never per criterion. Plain <c>ANALYZE</c>, these two tables only,
    /// schema-qualified; never <c>VACUUM ANALYZE</c> (a different change-reason).
    /// </para>
    /// </summary>
    public async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText =
            "ANALYZE public.company_watch_criterion_members;"
            + "ANALYZE public.company_watch_criterion_materialisations;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    // The jsonb_to_recordset row shape. The property name must match the recordset column name.
    private sealed record BatchRow(string organization_number)
    {
        // REDACTED (#883). The member name is snake_case because jsonb_to_recordset matches recordset
        // columns by property NAME, but the compiler-generated ToString() would then print a raw
        // org.nr for a plain {X} MEL placeholder. Pinned by OrgNrRecordLoggingGuardTests.
        public override string ToString() => "BatchRow(org.nr redacted)";
    }
}
