using System.Data;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1682 — raw SQL over the two profile tables and the one join that produces them. Raw Npgsql on the
/// concrete <see cref="AppDbContext"/>, parity <c>ScbCompanyRegisterStore</c> and
/// <c>CompanyWatchCriterionMemberStore</c>: the register is not on <c>IAppDbContext</c> and never will
/// be (ADR 0139), so the <c>job_ads ⋈ company_register</c> aggregate is computed here, entirely
/// server-side, and only counts come back. No org.nr ever reaches C# scope — the reason this store
/// is deliberately NOT on <c>OrganizationNumberSurfacingGuardTests.RawOrgNrReadingSourcePaths</c>;
/// the day an intermediate step reads one, that path is added the same day.
///
/// <para>
/// ADR 0113: the <c>job_ads</c> read below is raw SQL, so the IL scan sees no <c>get_JobAds</c> site;
/// it is declared in <c>JobAdLifecycleReadRegistry.KnownNonReaches</c> with its lifecycle decision
/// stated in words. That decision is <b>AnyStatus-as-allow-list</b>: every ad we have seen —
/// <c>Active</c> and <c>Archived</c> — counts (Klas 2026-09-14), and the list is POSITIVE so that the
/// <c>Erased</c> Art. 17 tombstone (#842), and any status added later, is excluded by construction
/// (JobAdSearchComposition #864 D4). The register side takes the same shape for the same reason
/// (<c>CompanyWatchBrowseQuery.FromWhere</c>): <c>Active</c> and <c>Deregistered</c> both count — a
/// company that has since been deregistered WAS the employer on those ads, and this is a
/// backward-looking aggregate, not a live watch (senior-cto-advisor D6) — and a third status the
/// register may gain one day lands in the not-in-register bucket rather than in a published number.
/// </para>
/// <para>
/// Commands set <see cref="CommandTimeoutSeconds"/> explicitly: a raw <c>NpgsqlCommand</c> does not
/// inherit EF's command timeout and would otherwise take the connection-string default (the trap
/// <c>CompanyWatchCriterionMemberStore</c> documents). Measured on the box 2026-09-14 the whole
/// aggregate is 163 ms over 83 280 ads (docs/reviews/2026-09-14-1682-profile-measurement.md §4);
/// 120 s is headroom for a hung statement to fail loud, never a budget.
/// </para>
/// </summary>
internal sealed class OccupationDivisionProfileStore(AppDbContext db)
{
    internal const int CommandTimeoutSeconds = 120;

    /// <summary>
    /// The aggregate, verbatim from the measurement report. <c>LEFT JOIN</c> so an ad whose employer is
    /// not in the register lands in its own bucket instead of vanishing — the single most consequential
    /// token in the statement (issue scope 1), pinned by a Testcontainers test that seeds exactly such
    /// an ad. <c>sni_codes[1]</c> is the primary code by the register's own convention: SCB's
    /// <c>Bransch_1..5</c> are read in order and blank slots are skipped
    /// (<c>ScbCompanyRegisterClient.MapRow</c>), so element one is the first populated slot, which is
    /// <c>Bransch_1</c> whenever it is present. <c>LEFT(code, 2)</c> on text, never arithmetic: SNI codes
    /// carry load-bearing leading zeros. Three arms, not two: an empty array must not fold into
    /// "not in register". No <c>ORDER BY</c> — the insert is a set.
    /// </summary>
    private const string RebuildSql = """
        INSERT INTO occupation_division_profiles (occupation_group_concept_id, division_code, ad_count)
        SELECT j.occupation_group_concept_id,
               CASE WHEN r.organization_number IS NULL THEN @not_in_register
                    WHEN COALESCE(array_length(r.sni_codes, 1), 0) = 0 THEN @no_sni
                    ELSE left(r.sni_codes[1], 2) END,
               count(*)
        FROM job_ads j
        LEFT JOIN company_register r
               ON r.organization_number = j.organization_number
              AND r.status = ANY(@register_statuses)
        WHERE j.status = ANY(@ad_statuses)
          AND j.occupation_group_concept_id IS NOT NULL
        GROUP BY 1, 2;
        """;

    private const string TallySql = """
        SELECT count(DISTINCT occupation_group_concept_id)::int,
               count(*)::int,
               COALESCE(sum(ad_count), 0)::int,
               COALESCE(sum(ad_count) FILTER (WHERE division_code = @not_in_register), 0)::int,
               COALESCE(sum(ad_count) FILTER (WHERE division_code = @no_sni), 0)::int
        FROM occupation_division_profiles;
        """;

    private const string UpsertRunSql = """
        INSERT INTO occupation_division_profile_runs (
            profile_key, profiled_at, occupation_groups_profiled, rows_written, ads_counted,
            ads_not_in_register, ads_in_register_without_sni)
        VALUES (@profile_key, @profiled_at, @groups, @rows, @ads, @not_in_register_ads, @no_sni_ads)
        ON CONFLICT (profile_key) DO UPDATE SET
            profiled_at                 = EXCLUDED.profiled_at,
            occupation_groups_profiled  = EXCLUDED.occupation_groups_profiled,
            rows_written                = EXCLUDED.rows_written,
            ads_counted                 = EXCLUDED.ads_counted,
            ads_not_in_register         = EXCLUDED.ads_not_in_register,
            ads_in_register_without_sni = EXCLUDED.ads_in_register_without_sni;
        """;

    /// <summary>
    /// Replace, never supplement: an unconditional <c>DELETE</c>, the aggregate insert, the tally and
    /// the run-row upsert, all in ONE transaction, so a crash between them can never leave a profile
    /// beside a run row that disagrees with it (the <c>ReplaceAsync</c> discipline).
    /// </summary>
    public async Task<RebuildOutcome> RebuildAsync(DateTimeOffset profiledAt, CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandTimeout = CommandTimeoutSeconds;
            deleteCmd.CommandText = "DELETE FROM occupation_division_profiles;";
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.Transaction = transaction;
            insertCmd.CommandTimeout = CommandTimeoutSeconds;
            insertCmd.CommandText = RebuildSql;
            insertCmd.Parameters.AddWithValue(
                "@not_in_register", NpgsqlDbType.Text, OccupationDivisionProfileRow.NotInRegisterCode);
            insertCmd.Parameters.AddWithValue(
                "@no_sni", NpgsqlDbType.Text, OccupationDivisionProfileRow.NoSniCode);
            insertCmd.Parameters.AddWithValue(
                "@register_statuses", NpgsqlDbType.Array | NpgsqlDbType.Text, CountedRegisterStatuses);
            insertCmd.Parameters.AddWithValue(
                "@ad_statuses", NpgsqlDbType.Array | NpgsqlDbType.Text, CountedAdStatuses);
            await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        RebuildOutcome outcome;
        await using (var tallyCmd = connection.CreateCommand())
        {
            tallyCmd.Transaction = transaction;
            tallyCmd.CommandTimeout = CommandTimeoutSeconds;
            tallyCmd.CommandText = TallySql;
            tallyCmd.Parameters.AddWithValue(
                "@not_in_register", NpgsqlDbType.Text, OccupationDivisionProfileRow.NotInRegisterCode);
            tallyCmd.Parameters.AddWithValue(
                "@no_sni", NpgsqlDbType.Text, OccupationDivisionProfileRow.NoSniCode);
            await using var reader = await tallyCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Profilens tally-fråga gav ingen rad; en aggregatfråga utan GROUP BY ger alltid en.");
            outcome = new RebuildOutcome(
                OccupationGroupsProfiled: reader.GetInt32(0),
                RowsWritten: reader.GetInt32(1),
                AdsCounted: reader.GetInt32(2),
                AdsNotInRegister: reader.GetInt32(3),
                AdsInRegisterWithoutSni: reader.GetInt32(4));
        }

        await using (var runCmd = connection.CreateCommand())
        {
            runCmd.Transaction = transaction;
            runCmd.CommandTimeout = CommandTimeoutSeconds;
            runCmd.CommandText = UpsertRunSql;
            runCmd.Parameters.AddWithValue("@profile_key", NpgsqlDbType.Text, OccupationDivisionProfileRun.CurrentKey);
            runCmd.Parameters.AddWithValue("@profiled_at", NpgsqlDbType.TimestampTz, profiledAt);
            runCmd.Parameters.AddWithValue("@groups", NpgsqlDbType.Integer, outcome.OccupationGroupsProfiled);
            runCmd.Parameters.AddWithValue("@rows", NpgsqlDbType.Integer, outcome.RowsWritten);
            runCmd.Parameters.AddWithValue("@ads", NpgsqlDbType.Integer, outcome.AdsCounted);
            runCmd.Parameters.AddWithValue("@not_in_register_ads", NpgsqlDbType.Integer, outcome.AdsNotInRegister);
            runCmd.Parameters.AddWithValue("@no_sni_ads", NpgsqlDbType.Integer, outcome.AdsInRegisterWithoutSni);
            await runCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>
    /// AGENTS.md §3.6, all three conditions evaluated rather than inherited: one periodic writer (this
    /// job), read-only between runs, and <c>occupation_group_concept_id</c> reaches a <c>WHERE</c>.
    /// </summary>
    public async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        cmd.CommandText = "ANALYZE public.occupation_division_profiles;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The read: the run row first, gated on age, and only then the rows for the requested groups.
    /// An absent or over-age run costs no scan at all — the profile table is not touched — and
    /// answers with a null <see cref="ProfileRead.ProfiledAt"/>, which the query port turns into
    /// <c>NotProfiled</c>. Rows come back raw, sentinels included; the port owns the bucket mapping.
    /// </summary>
    public async Task<ProfileRead> ReadAsync(
        IReadOnlyList<string> occupationGroupConceptIds,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occupationGroupConceptIds);
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        DateTimeOffset? profiledAt;
        await using (var runCmd = connection.CreateCommand())
        {
            runCmd.CommandTimeout = CommandTimeoutSeconds;
            runCmd.CommandText = """
                SELECT profiled_at FROM occupation_division_profile_runs
                WHERE profile_key = @profile_key AND profiled_at >= @not_before;
                """;
            runCmd.Parameters.AddWithValue("@profile_key", NpgsqlDbType.Text, OccupationDivisionProfileRun.CurrentKey);
            runCmd.Parameters.AddWithValue("@not_before", NpgsqlDbType.TimestampTz, notBefore);
            var scalar = await runCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            profiledAt = scalar is DateTime dt ? new DateTimeOffset(dt, TimeSpan.Zero) : null;
        }

        if (profiledAt is null || occupationGroupConceptIds.Count == 0)
            return new ProfileRead(profiledAt, []);

        var rows = new List<ProfileRowRead>();
        await using (var rowsCmd = connection.CreateCommand())
        {
            rowsCmd.CommandTimeout = CommandTimeoutSeconds;
            rowsCmd.CommandText = """
                SELECT occupation_group_concept_id, division_code, ad_count
                FROM occupation_division_profiles
                WHERE occupation_group_concept_id = ANY(@ids);
                """;
            rowsCmd.Parameters.AddWithValue(
                "@ids", NpgsqlDbType.Array | NpgsqlDbType.Text, occupationGroupConceptIds.ToArray());
            await using var reader = await rowsCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                rows.Add(new ProfileRowRead(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return new ProfileRead(profiledAt, rows);
    }

    /// <summary>The positive allow-list on the ad side. Both members count; nothing else does.</summary>
    private static readonly string[] CountedAdStatuses =
        [JobAdStatus.Active.Value, JobAdStatus.Archived.Value];

    /// <summary>The positive allow-list on the register side, stored by name (the column's mapping).</summary>
    private static readonly string[] CountedRegisterStatuses =
        [nameof(CompanyRegisterStatus.Active), nameof(CompanyRegisterStatus.Deregistered)];

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    internal sealed record RebuildOutcome(
        int OccupationGroupsProfiled,
        int RowsWritten,
        int AdsCounted,
        int AdsNotInRegister,
        int AdsInRegisterWithoutSni);

    internal sealed record ProfileRead(DateTimeOffset? ProfiledAt, IReadOnlyList<ProfileRowRead> Rows);

    internal sealed record ProfileRowRead(string OccupationGroupConceptId, string DivisionCode, int AdCount);
}
