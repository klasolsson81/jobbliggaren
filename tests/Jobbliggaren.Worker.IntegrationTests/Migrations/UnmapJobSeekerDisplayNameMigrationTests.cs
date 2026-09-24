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
/// #1742 (epic #1732 part 4b, PR U) — <c>20260924182548_UnmapJobSeekerDisplayName</c> applies and
/// reverses against a real Postgres, as the app role, on a table that still physically carries
/// <c>display_name</c>.
///
/// <para>
/// This migration's <c>Up</c>/<c>Down</c> are both empty (Parallel Change — see the migration's own
/// docblock): from here on the model no longer maps the column, but the column itself, and any name
/// already stored in it, must survive untouched. The journey therefore seeds a row with a name in the
/// shape the table held before #1741 PR B (<c>d8959385</c>) — the last writer of that column, now
/// retired — migrates forward across this migration, migrates back, and forward again, asserting at
/// every stop that the column's shape AND the row's value are exactly what they were before this
/// migration ran.
/// </para>
///
/// <para>
/// The journey stops at <see cref="ThisMigration"/>, never at the assembly's head, so this test stays
/// meaningful once #1742 adds the migration that drops the column. Own container, one journey method — the
/// <see cref="Jobbliggaren.Worker.IntegrationTests.Migrations.DisplayNameNullableMigrationTests"/>
/// form: xunit news a class instance per test method, so one method is one container, and
/// <see cref="TestDatabaseProvisioner"/> provisions the database so the migration runs as the role
/// production migrates with.
/// </para>
/// </summary>
public sealed class UnmapJobSeekerDisplayNameMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20260924182548_UnmapJobSeekerDisplayName";
    private const string PreviousMigration = "20260920232535_DisplayNameNullable";

    private const string DisplayNameColumn = "display_name";

    /// <summary>Non-ASCII on purpose: a backfill, truncation or re-encode shows up in the round trip.</summary>
    private const string SurvivingName = "Åsa Öberg-Lindqvist";

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

    private sealed record ColumnShape(string DataType, bool IsNullable, int? MaxLength, string? Default);

    private async Task<ColumnShape?> ReadDisplayNameColumnAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT data_type, is_nullable, character_maximum_length, column_default
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'job_seekers' AND column_name = @name
            """,
            conn);
        cmd.Parameters.AddWithValue("name", DisplayNameColumn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new ColumnShape(
            reader.GetString(0),
            reader.GetString(1) == "YES",
            await reader.IsDBNullAsync(2, ct) ? null : reader.GetInt32(2),
            await reader.IsDBNullAsync(3, ct) ? null : reader.GetString(3));
    }

    /// <summary>
    /// Reads the name as the table holds it, distinguishing SQL NULL from every string value.
    /// </summary>
    private async Task<(bool Found, string? Name)> ReadDisplayNameAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT {DisplayNameColumn} FROM job_seekers WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return (false, null);

        return (true, await reader.IsDBNullAsync(0, ct) ? null : reader.GetString(0));
    }

    /// <summary>
    /// A named row in the shape the table held before #1741 PR B (<c>d8959385</c>) retired the last
    /// writer of <c>display_name</c> — raw SQL because, from this migration on, no writer in
    /// <c>src/</c> can produce this shape any more.
    /// </summary>
    private async Task<Guid> InsertNamedSeekerAsync(CancellationToken ct)
    {
        var id = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO job_seekers (id, user_id, display_name, preferences, created_at)
            VALUES (@id, @user_id, NULL, '{"Language":"sv"}'::jsonb, now())
            """,
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("user_id", Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync(ct);

        await using var db = NewAppContext();
        await LegacyAccountName.WriteAsync(db, id, SurvivingName, ct);

        return id;
    }

    [Fact]
    public async Task UnmapJobSeekerDisplayName_LeavesTheColumnAndItsValueUntouched_AcrossUpAndDown()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewAppContext();

        var assembly = db.Database.GetMigrations().ToList();
        assembly.ShouldContain(ThisMigration);
        assembly.ShouldContain(PreviousMigration);

        // --- 1. Stop one short of this migration, and seed a named row in the shape the table held
        // before #1741 PR B.
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);
        var namedId = await InsertNamedSeekerAsync(ct);
        (await ReadDisplayNameAsync(namedId, ct)).ShouldBe((true, SurvivingName));

        // --- 2. Forward across this migration: Up() is empty, so the column and the row's value must
        // be exactly what they were.
        await db.GetService<IMigrator>().MigrateAsync(ThisMigration, ct);

        var atThisMigration = await ReadDisplayNameColumnAsync(ct);
        atThisMigration.ShouldNotBeNull();
        atThisMigration.IsNullable.ShouldBeTrue();
        atThisMigration.DataType.ShouldBe("character varying");
        atThisMigration.MaxLength.ShouldBe(200);
        atThisMigration.Default.ShouldBeNull();
        (await ReadDisplayNameAsync(namedId, ct)).ShouldBe((true, SurvivingName));

        // --- 3. Back to the previous migration: Down() is equally empty, so the same holds in reverse.
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);

        var atPrevious = await ReadDisplayNameColumnAsync(ct);
        atPrevious.ShouldNotBeNull();
        atPrevious.IsNullable.ShouldBeTrue();
        atPrevious.DataType.ShouldBe("character varying");
        atPrevious.MaxLength.ShouldBe(200);
        atPrevious.Default.ShouldBeNull();
        (await ReadDisplayNameAsync(namedId, ct)).ShouldBe((true, SurvivingName));
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldNotContain(ThisMigration);

        // --- 4. Forward again: the shape a re-deploy runs, still unchanged.
        await db.GetService<IMigrator>().MigrateAsync(ThisMigration, ct);

        var backAtThisMigration = await ReadDisplayNameColumnAsync(ct);
        backAtThisMigration.ShouldNotBeNull();
        backAtThisMigration.IsNullable.ShouldBeTrue();
        backAtThisMigration.DataType.ShouldBe("character varying");
        backAtThisMigration.MaxLength.ShouldBe(200);
        backAtThisMigration.Default.ShouldBeNull();
        (await db.Database.GetAppliedMigrationsAsync(ct)).Last().ShouldBe(ThisMigration);
        (await ReadDisplayNameAsync(namedId, ct)).ShouldBe((true, SurvivingName));
    }
}
