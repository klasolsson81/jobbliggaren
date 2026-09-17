using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// #1736 (ADR 0142 D6) — <c>20260917153605_AddTermsAcceptanceToJobSeeker</c> applies and REVERSES
/// against a real Postgres, as the app role, under production's Phase A privilege posture — and
/// applies to a POPULATED table, which is the case the deploy meets.
///
/// <para>
/// The Up is three nullable <c>AddColumn</c>s plus the all-or-nothing check constraint; the Down
/// drops all four. The Down is the half nothing else exercises: a rollback is the operator's only
/// way out of a bad deploy, and an EF-scaffolded Down that does not actually run is discovered at the
/// worst possible moment. So the journey is head → previous → head, the columns and the constraint
/// are read out of the catalog at each stop rather than inferred from the migration file, and a row
/// inserted while at the previous migration rides the forward step: its three columns must come out
/// NULL. A column default or a backfill on any of the three would stamp an acceptance that never
/// happened onto the accounts that existed before 1b — the false Art. 5(2) record the columns exist
/// to prevent — and nothing else in the suite would notice (test-writer Major, PR #1751).
/// </para>
///
/// <para>
/// Own container, one journey method — the <c>SchemaAheadSubstrateTests</c> form: xunit news a class
/// instance per test method, so one method is one container, and the database is provisioned by
/// <see cref="TestDatabaseProvisioner"/> so the migration runs as the role production migrates with.
/// </para>
/// </summary>
public sealed class AddTermsAcceptanceToJobSeekerMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20260917153605_AddTermsAcceptanceToJobSeeker";
    private const string PreviousMigration = "20260914134635_AddOccupationDivisionProfile";

    private const string AcceptedAtColumn = "terms_accepted_at";
    private const string TermsVersionColumn = "terms_version";
    private const string PrivacyPolicyVersionColumn = "privacy_policy_version";
    private const string AllOrNothingConstraint = "ck_job_seekers_terms_all_or_none";

    // Ordinal order, because it doubles as the expected set of an OrderBy(StringComparer.Ordinal).
    private static readonly string[] AddedColumns =
        [PrivacyPolicyVersionColumn, AcceptedAtColumn, TermsVersionColumn];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private string _appConnectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _appConnectionString = await TestDatabaseProvisioner
            .ProvisionAndGetAppConnectionStringAsync(_postgres.GetConnectionString());

        // pg_trgm is required by the job-ad trigram-index migration, and the app role cannot create
        // it (no CREATE on the database); production issues it in `ensure-extensions` mode under
        // master credentials before `schema` runs, so the superuser stands in for that mode here.
        await using var superuser = new NpgsqlConnection(_postgres.GetConnectionString());
        await superuser.OpenAsync();
        await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm;", superuser);
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private sealed record ColumnShape(string DataType, bool IsNullable, int? MaxLength);

    private async Task<Dictionary<string, ColumnShape>> ReadAddedColumnsAsync(CancellationToken ct)
    {
        var columns = new Dictionary<string, ColumnShape>(StringComparer.Ordinal);

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT column_name, data_type, is_nullable, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'job_seekers' AND column_name = ANY(@names)
            """,
            conn);
        cmd.Parameters.AddWithValue("names", AddedColumns);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns[reader.GetString(0)] = new ColumnShape(
                reader.GetString(1),
                reader.GetString(2) == "YES",
                await reader.IsDBNullAsync(3, ct) ? null : reader.GetInt32(3));
        }

        return columns;
    }

    private async Task<List<string>> ReadCheckConstraintsAsync(CancellationToken ct)
    {
        var names = new List<string>();

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT conname
            FROM pg_constraint
            WHERE conrelid = 'public.job_seekers'::regclass AND contype = 'c'
            ORDER BY conname
            """,
            conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));

        return names;
    }

    /// <summary>
    /// A row in the shape the table holds at <see cref="PreviousMigration"/> — the accounts that
    /// exist before 1b deploys. The NOT NULL set there is id, user_id, display_name, preferences (the
    /// owned Preferences container) and created_at; match_preferences carries a column default; no
    /// terms column exists yet to write.
    /// </summary>
    private async Task<Guid> InsertPreMigrationRowAsync(CancellationToken ct)
    {
        var id = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO job_seekers (id, user_id, display_name, preferences, created_at)
            VALUES (@id, @user_id, 'Pre-migration Row', '{"Language":"sv"}'::jsonb, now())
            """,
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("user_id", Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync(ct);

        return id;
    }

    private async Task<(bool AcceptedAt, bool TermsVersion, bool PrivacyPolicyVersion)> ReadTermsColumnsAreNullAsync(
        Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT terms_accepted_at IS NULL, terms_version IS NULL, privacy_policy_version IS NULL
            FROM job_seekers
            WHERE id = @id
            """,
            conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).ShouldBeTrue("the pre-migration row must survive the forward step");
        return (reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    [Fact]
    public async Task AddTermsAcceptanceToJobSeeker_AddsThreeNullableColumnsAndTheConstraint_LeavesAPreExistingRowUnstamped_AndReverses()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewAppContext();

        var assembly = db.Database.GetMigrations().ToList();
        assembly.ShouldContain(ThisMigration);
        assembly.ShouldContain(PreviousMigration);

        // --- 1. Head: the three columns exist, nullable, with the declared types; the constraint too.
        await db.Database.MigrateAsync(ct);

        var atHead = await ReadAddedColumnsAsync(ct);
        atHead.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(AddedColumns);

        atHead[AcceptedAtColumn].DataType.ShouldBe("timestamp with time zone");
        atHead[AcceptedAtColumn].IsNullable.ShouldBeTrue();

        atHead[TermsVersionColumn].DataType.ShouldBe("character varying");
        atHead[TermsVersionColumn].IsNullable.ShouldBeTrue();
        atHead[TermsVersionColumn].MaxLength.ShouldBe(20);

        atHead[PrivacyPolicyVersionColumn].DataType.ShouldBe("character varying");
        atHead[PrivacyPolicyVersionColumn].IsNullable.ShouldBeTrue();
        atHead[PrivacyPolicyVersionColumn].MaxLength.ShouldBe(20);

        (await ReadCheckConstraintsAsync(ct)).ShouldContain(AllOrNothingConstraint);

        // --- 2. Rollback to the migration before this one: the columns and the constraint are gone.
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);

        (await ReadAddedColumnsAsync(ct)).ShouldBeEmpty();
        (await ReadCheckConstraintsAsync(ct)).ShouldNotContain(AllOrNothingConstraint);

        // --- 2b. A row that exists before the migration — the deploy's case, not an empty table.
        var legacyId = await InsertPreMigrationRowAsync(ct);

        // --- 3. Forward again, on the populated table: the shape a re-deploy runs.
        await db.Database.MigrateAsync(ct);

        (await ReadAddedColumnsAsync(ct)).Keys
            .OrderBy(k => k, StringComparer.Ordinal).ShouldBe(AddedColumns);
        (await ReadCheckConstraintsAsync(ct)).ShouldContain(AllOrNothingConstraint);
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();

        // The EFFECT on that row, not the columns' shape: no stamp. A defaultValue on any of the
        // three AddColumns keeps is_nullable = YES and passes every other test in the suite; this
        // read is the one that fails. It also proves ADD CONSTRAINT validated the all-NULL legacy
        // shape — the constraint admits the rows the deploy will find.
        (await ReadTermsColumnsAreNullAsync(legacyId, ct)).ShouldBe((true, true, true));
    }
}
