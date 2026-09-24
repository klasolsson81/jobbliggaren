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
/// #1742 (epic #1732 part 4b, PR D) — <c>20260924203855_DropJobSeekerDisplayName</c> applies and
/// reverses against a real Postgres, as the app role, on a POPULATED table.
///
/// <para>
/// This is a DESTRUCTIVE migration: a single <c>DROP COLUMN</c> on the account's own table. The
/// journey seeds two rows in the shape the table holds at
/// <c>20260924182548_UnmapJobSeekerDisplayName</c> (the model no longer maps the column, but the
/// column and any value in it are still physically present) — one written by the retired name-writing
/// path, one with no name at all, mirroring the two-row shape
/// <see cref="DisplayNameNullableMigrationTests"/> already exercises one migration earlier — migrates
/// forward across the drop, back again, one migration further back still, and forward again, reading
/// every fact out of the catalog rather than inferring it from the migration file.
/// </para>
///
/// <para>
/// The interesting half is what the Down does NOT restore. It re-adds the column with the pre-drop
/// shape, but every row it restores comes back <c>NULL</c> — including the row that had a name before
/// the drop. That is what makes the next migration further back, <c>DisplayNameNullable</c>'s own
/// guarded Down, refuse: its guard counts nameless rows, and after this migration's Down every seeded
/// row is nameless, whether it started out that way or not.
/// </para>
///
/// <para>
/// Own container, one journey method — the <see cref="DropAuthProviderColumnsMigrationTests"/> form:
/// xunit news a class instance per test method, so one method is one container, and
/// <see cref="TestDatabaseProvisioner"/> provisions the database so the migration runs as the role
/// production migrates with.
/// </para>
/// </summary>
public sealed class DropJobSeekerDisplayNameMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20260924203855_DropJobSeekerDisplayName";
    private const string PreviousMigration = "20260924182548_UnmapJobSeekerDisplayName";
    private const string TwoMigrationsBack = "20260917153605_AddTermsAcceptanceToJobSeeker";

    private const string DisplayNameColumn = "display_name";

    /// <summary>Non-ASCII on purpose: a backfill, truncation or re-encode shows up in the round trip.</summary>
    private const string SurvivingName = "Björn Håkansson-Ek";

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
    /// The identifying column a dropped-and-restored row still carries once <c>display_name</c> is
    /// gone: proof that the row itself, not just SOME row, survived — matched against the
    /// <c>user_id</c> recorded when the row was seeded.
    /// </summary>
    private async Task<Guid?> ReadUserIdAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT user_id FROM job_seekers WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid userId ? userId : null;
    }

    /// <summary>
    /// Reads the name as the table holds it, distinguishing SQL NULL from every string value. Only
    /// callable while the column exists.
    /// </summary>
    private async Task<string?> ReadDisplayNameAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT {DisplayNameColumn} FROM job_seekers WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>
    /// A named row in the shape the table held before #1741 PR B (<c>d8959385</c>) retired the last
    /// writer of <c>display_name</c> — raw SQL, because from that PR on no writer in <c>src/</c> can
    /// produce this shape any more.
    /// </summary>
    private async Task<(Guid Id, Guid UserId)> InsertNamedSeekerAsync(CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO job_seekers (id, user_id, display_name, preferences, created_at)
            VALUES (@id, @user_id, NULL, '{"Language":"sv"}'::jsonb, now())
            """,
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("user_id", userId);
        await cmd.ExecuteNonQueryAsync(ct);

        await using var db = NewAppContext();
        await LegacyAccountName.WriteAsync(db, id, SurvivingName, ct);

        return (id, userId);
    }

    /// <summary>
    /// A nameless row — the state <c>JobSeeker.Register</c> has written since #1737
    /// (<c>DisplayNameNullable</c>) — inserted directly, since no writer produces a name to null out.
    /// </summary>
    private async Task<(Guid Id, Guid UserId)> InsertNamelessSeekerAsync(CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO job_seekers (id, user_id, display_name, preferences, created_at)
            VALUES (@id, @user_id, NULL, '{"Language":"sv"}'::jsonb, now())
            """,
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("user_id", userId);
        await cmd.ExecuteNonQueryAsync(ct);

        return (id, userId);
    }

    [Fact]
    public async Task DropJobSeekerDisplayName_RemovesTheColumnButKeepsTheRows_AndForecloses_DisplayNameNullablesDown()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewAppContext();

        var assembly = db.Database.GetMigrations().ToList();
        assembly.ShouldContain(ThisMigration);
        assembly.ShouldContain(PreviousMigration);
        assembly.ShouldContain(TwoMigrationsBack);

        // --- 1. Stop one short of this migration, and seed two rows in the shape the table held
        // there: one written by the retired name-writing path, one with no name at all.
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);

        var (namedId, namedUserId) = await InsertNamedSeekerAsync(ct);
        var (namelessId, namelessUserId) = await InsertNamelessSeekerAsync(ct);

        (await ReadDisplayNameAsync(namedId, ct)).ShouldBe(SurvivingName);
        (await ReadDisplayNameAsync(namelessId, ct)).ShouldBeNull();

        // --- 2. Forward across the drop: the column is gone from the catalog, but both rows survive —
        // read back by an identifying column the drop did not touch, not merely by presence.
        await db.GetService<IMigrator>().MigrateAsync(ThisMigration, ct);

        (await ReadDisplayNameColumnAsync(ct)).ShouldBeNull();
        (await ReadUserIdAsync(namedId, ct)).ShouldBe(namedUserId);
        (await ReadUserIdAsync(namelessId, ct)).ShouldBe(namelessUserId);

        // --- 3. Down to the previous migration: the column reappears with its pre-drop shape, but
        // restores SHAPE only — both rows, including the one that had a name, come back NULL. This is
        // the fact the class doc and the migration's own docblock make: the drop is the erasure, and
        // its Down cannot undo that.
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);

        var restoredColumn = await ReadDisplayNameColumnAsync(ct);
        restoredColumn.ShouldNotBeNull();
        restoredColumn.IsNullable.ShouldBeTrue();
        restoredColumn.DataType.ShouldBe("character varying");
        restoredColumn.MaxLength.ShouldBe(200);
        restoredColumn.Default.ShouldBeNull(
            "a default here would let an insert without a name silently become ''");

        (await ReadDisplayNameAsync(namedId, ct)).ShouldBeNull("the Down restores shape, never data");
        (await ReadDisplayNameAsync(namelessId, ct)).ShouldBeNull();

        // --- 4. One migration further back: DisplayNameNullable's own guarded Down refuses, because
        // every row is now nameless — the row that originally had a name included. The message's count
        // is both seeded rows, not one.
        var refusal = await Should.ThrowAsync<PostgresException>(() =>
            db.GetService<IMigrator>().MigrateAsync(TwoMigrationsBack, ct));

        refusal.SqlState.ShouldBe(PostgresErrorCodes.RaiseException);
        refusal.MessageText.ShouldContain("DisplayNameNullable Down");
        refusal.MessageText.ShouldContain("2 job_seekers row(s) have no display_name");

        // The refusal left the SCHEMA and both rows exactly where step 3 left them — no DDL runs once
        // the guard raises. The applied-migrations ledger is one step further back than
        // PreviousMigration, though: EF applies each migration in the walk-down in its own
        // transaction, so UnmapJobSeekerDisplayName's own (no-op) Down already committed and was
        // removed from the ledger before DisplayNameNullable's guarded Down ran and failed —
        // measured, not assumed. DisplayNameNullable itself stays applied, because its Down's
        // transaction is the one that rolled back.
        var afterRefusal = await ReadDisplayNameColumnAsync(ct);
        afterRefusal.ShouldNotBeNull();
        afterRefusal.IsNullable.ShouldBeTrue();
        afterRefusal.Default.ShouldBeNull();
        (await ReadDisplayNameAsync(namedId, ct)).ShouldBeNull();
        (await ReadDisplayNameAsync(namelessId, ct)).ShouldBeNull();
        (await ReadUserIdAsync(namedId, ct)).ShouldBe(namedUserId);
        (await ReadUserIdAsync(namelessId, ct)).ShouldBe(namelessUserId);
        var appliedAfterRefusal = await db.Database.GetAppliedMigrationsAsync(ct);
        appliedAfterRefusal.ShouldNotContain(PreviousMigration);
        appliedAfterRefusal.ShouldContain("20260920232535_DisplayNameNullable");
        appliedAfterRefusal.ShouldContain(TwoMigrationsBack);
        appliedAfterRefusal.ShouldNotContain(ThisMigration);

        // --- 5. Forward again, on the populated table: the shape a re-deploy runs.
        await db.GetService<IMigrator>().MigrateAsync(ThisMigration, ct);

        (await ReadDisplayNameColumnAsync(ct)).ShouldBeNull();
        (await ReadUserIdAsync(namedId, ct)).ShouldBe(namedUserId);
        (await ReadUserIdAsync(namelessId, ct)).ShouldBe(namelessUserId);
        (await db.Database.GetAppliedMigrationsAsync(ct)).Last().ShouldBe(ThisMigration);
    }
}
