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
/// #1736 (ADR 0142 D6) — <c>20260917141433_AddTermsAcceptanceToJobSeeker</c> applies and REVERSES
/// against a real Postgres, as the app role, under production's Phase A privilege posture.
///
/// <para>
/// The Up is three nullable <c>AddColumn</c>s and the Down three <c>DropColumn</c>s. The Down is the
/// half nothing else exercises: a rollback is the operator's only way out of a bad deploy, and an
/// EF-scaffolded Down that does not actually run is discovered at the worst possible moment. So the
/// journey is head → previous → head, and the columns are read out of
/// <c>information_schema.columns</c> at each stop rather than inferred from the migration file.
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
    private const string ThisMigration = "20260917141433_AddTermsAcceptanceToJobSeeker";
    private const string PreviousMigration = "20260914134635_AddOccupationDivisionProfile";

    private const string AcceptedAtColumn = "terms_accepted_at";
    private const string TermsVersionColumn = "terms_version";
    private const string PrivacyPolicyVersionColumn = "privacy_policy_version";

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

    [Fact]
    public async Task AddTermsAcceptanceToJobSeeker_AddsThreeNullableColumns_AndDropsThemOnRollback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewAppContext();

        var assembly = db.Database.GetMigrations().ToList();
        assembly.ShouldContain(ThisMigration);
        assembly.ShouldContain(PreviousMigration);

        // --- 1. Head: the three columns exist, nullable, with the declared types. ---------------
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

        // Nullable is the whole back-compat premise: the columns were added to a table that already
        // had rows, so a NOT NULL here would have failed the deploy against a populated database.
        // TermsAcceptanceBackcompatTests asserts the read side of the same fact.

        // --- 2. Rollback to the migration before this one: the columns are gone. ----------------
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);

        (await ReadAddedColumnsAsync(ct)).ShouldBeEmpty();

        // --- 3. Forward again: re-applying after a rollback is the shape a re-deploy runs. ------
        await db.Database.MigrateAsync(ct);

        (await ReadAddedColumnsAsync(ct)).Keys
            .OrderBy(k => k, StringComparer.Ordinal).ShouldBe(AddedColumns);
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();
    }
}
