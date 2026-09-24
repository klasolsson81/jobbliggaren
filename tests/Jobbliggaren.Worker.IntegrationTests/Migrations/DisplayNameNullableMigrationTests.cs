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
/// #1737 (epic #1732 part 1c, ADR 0142 D7) — <c>20260920232535_DisplayNameNullable</c> applies and
/// REVERSES against a real Postgres, as the app role, on a populated table.
///
/// <para>
/// The <c>Down</c> is the half worth a journey. EF's scaffold for this reversal backfills every NULL
/// with <c>''</c> and leaves a column default behind, so this one refuses instead. The journey
/// therefore rolls back WHILE a nameless row
/// exists (must raise, and leave the schema and the row exactly as they were), removes that row the
/// way an operator would, and rolls back again (must succeed, with no default, and the surviving
/// name unchanged). The column is read out of <c>information_schema</c> at each stop rather than
/// inferred from the migration file.
/// </para>
///
/// <para>
/// The journey stops at <see cref="ThisMigration"/>, never at the assembly's head: from #1742 on the
/// model no longer maps the column, and a later migration drops it. So both rows are written with raw
/// SQL in the shape the table holds at this migration. The nameless row is the state
/// <c>JobSeeker.Register</c> has written since #1737. The named row is the state every writer before
/// #1741 PR B (<c>d8959385</c>) left behind; none of them exists any more.
/// </para>
///
/// <para>
/// Own container, one journey method — the <see cref="AddTermsAcceptanceToJobSeekerMigrationTests"/>
/// form: xunit news a class instance per test method, so one method is one container, and
/// <see cref="TestDatabaseProvisioner"/> provisions the database so the migration runs as the role
/// production migrates with.
/// </para>
/// </summary>
public sealed class DisplayNameNullableMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20260920232535_DisplayNameNullable";
    private const string PreviousMigration = "20260917153605_AddTermsAcceptanceToJobSeeker";

    private const string DisplayNameColumn = "display_name";

    /// <summary>Non-ASCII on purpose: a backfill or a re-encode shows up in the round trip.</summary>
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
    /// Reads the name as the table holds it, distinguishing SQL NULL from every string value —
    /// including the empty one, which is what a scaffolded backfill would have left behind.
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
    /// A row in the shape the table holds at <see cref="ThisMigration"/>. The NOT NULL set there is
    /// id, user_id, preferences and created_at; match_preferences carries a column default, and the
    /// three terms columns stay NULL together, which their CHECK admits.
    /// </summary>
    private async Task<Guid> InsertSeekerAsync(string? displayName, CancellationToken ct)
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

        if (displayName is not null)
        {
            await using var db = NewAppContext();
            await LegacyAccountName.WriteAsync(db, id, displayName, ct);
        }

        return id;
    }

    private async Task DeleteSeekerAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM job_seekers WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        (await cmd.ExecuteNonQueryAsync(ct)).ShouldBe(1);
    }

    [Fact]
    public async Task DisplayNameNullable_AdmitsANamelessRow_AndItsDownRefusesToInventAName_BeforeReversingCleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewAppContext();

        var assembly = db.Database.GetMigrations().ToList();
        assembly.ShouldContain(ThisMigration);
        assembly.ShouldContain(PreviousMigration);

        // --- 1. At this migration: the column is nullable, unchanged in type and width, and carries
        // no default.
        await db.GetService<IMigrator>().MigrateAsync(ThisMigration, ct);

        var atThisMigration = await ReadDisplayNameColumnAsync(ct);
        atThisMigration.ShouldNotBeNull();
        atThisMigration.IsNullable.ShouldBeTrue();
        atThisMigration.DataType.ShouldBe("character varying");
        atThisMigration.MaxLength.ShouldBe(200);
        atThisMigration.Default.ShouldBeNull("a default would hand every future insert a name nobody typed");

        // --- 2. Two rows. The nameless one is the state the Up exists for.
        var namedId = await InsertSeekerAsync(SurvivingName, ct);
        var namelessId = await InsertSeekerAsync(null, ct);

        (await ReadDisplayNameAsync(namelessId, ct)).ShouldBe((true, null));
        (await ReadDisplayNameAsync(namedId, ct)).ShouldBe((true, SurvivingName));

        // --- 3. Rollback while that row exists: the Down refuses, by its own guard rather than by
        // Postgres rejecting the NOT NULL, and the count in the message is what tells the two apart.
        var refusal = await Should.ThrowAsync<PostgresException>(() =>
            db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct));

        refusal.SqlState.ShouldBe(PostgresErrorCodes.RaiseException);
        refusal.MessageText.ShouldContain("DisplayNameNullable Down");
        refusal.MessageText.ShouldContain("1 job_seekers row(s) have no display_name");

        // The refusal left nothing half-done: schema, row and history entry are all where they were
        // before the attempt. Nothing above would have noticed a Down that failed partway instead.
        var afterRefusal = await ReadDisplayNameColumnAsync(ct);
        afterRefusal.ShouldNotBeNull();
        afterRefusal.IsNullable.ShouldBeTrue();
        afterRefusal.Default.ShouldBeNull();
        (await ReadDisplayNameAsync(namelessId, ct)).ShouldBe((true, null));
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldContain(ThisMigration);

        // --- 4. The operator's way out, which the message asks for: resolve the row, then retry.
        await DeleteSeekerAsync(namelessId, ct);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);

        var atPrevious = await ReadDisplayNameColumnAsync(ct);
        atPrevious.ShouldNotBeNull();
        atPrevious.IsNullable.ShouldBeFalse();
        atPrevious.DataType.ShouldBe("character varying");
        atPrevious.MaxLength.ShouldBe(200);
        atPrevious.Default.ShouldBeNull(
            "a default here would let an insert without a name silently become ''");
        (await db.Database.GetAppliedMigrationsAsync(ct)).ShouldNotContain(ThisMigration);

        // The EFFECT on the row that stayed, not the column's shape: no backfill reached a name that
        // was already there. Every catalog read above passes with or without one.
        (await ReadDisplayNameAsync(namedId, ct)).ShouldBe((true, SurvivingName));

        // --- 5. Forward again on the populated table: the shape a re-deploy runs.
        await db.GetService<IMigrator>().MigrateAsync(ThisMigration, ct);

        var backAtThisMigration = await ReadDisplayNameColumnAsync(ct);
        backAtThisMigration.ShouldNotBeNull();
        backAtThisMigration.IsNullable.ShouldBeTrue();
        backAtThisMigration.MaxLength.ShouldBe(200);
        backAtThisMigration.Default.ShouldBeNull();
        (await db.Database.GetAppliedMigrationsAsync(ct)).Last().ShouldBe(ThisMigration);
        (await ReadDisplayNameAsync(namedId, ct)).ShouldBe((true, SurvivingName));
    }
}
