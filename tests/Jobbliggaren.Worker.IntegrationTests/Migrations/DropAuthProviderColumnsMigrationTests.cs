using Jobbliggaren.Infrastructure.Identity;
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
/// #1747 (epic #1732 part 6d, ADR 0142 D8 + ADR 0017's 2026-09-17 amendment) —
/// <c>20260917195454_DropAuthProviderColumns</c> applies and REVERSES against a real Postgres,
/// as the app role, under production's Phase A privilege posture, on a POPULATED table.
///
/// <para>
/// This is a DESTRUCTIVE migration: two <c>DROP COLUMN</c>s and a <c>DROP INDEX</c> on the table
/// that holds every account. Three things need an oracle, and none of them has one anywhere else.
/// <b>(1) The rows survive.</b> <c>DROP COLUMN</c> narrows a table; a mistake that reaches for the
/// table instead — or a cascade off the dropped unique index — would empty <c>AspNetUsers</c> on a
/// live deploy, and no other test in the suite has a populated identity table at migration time.
/// <b>(2) The <c>Down</c> actually runs.</b> A rollback is the operator's only way out of a bad
/// deploy, and an EF-scaffolded <c>Down</c> that throws is discovered at the worst possible moment.
/// <b>(3) The index leaves the catalog.</b> <c>DropIndex</c> names the index as a string; a name
/// that does not match is a silent no-op in the migration file and a surviving unique constraint in
/// the database, which 6a would then collide with when it starts writing logins.
/// </para>
///
/// <para>
/// So the journey is head → previous → head, everything is read out of the catalog rather than
/// inferred from the migration file, and a row inserted while at the previous migration rides the
/// forward step.
/// </para>
///
/// <para>
/// <b>On the inserted row's premise (CLAUDE.md §5 <c>Tests:</c>).</b> What the assertion rests on is
/// that the row EXISTS across the forward step — a state every registration produces through
/// <c>UserManager.CreateAsync</c>. Its <c>provider</c>/<c>provider_user_id</c> values are incidental
/// to that, but they are written in the deploy's own shape anyway: <c>'Local'</c> and NULL is what
/// rows carry at <see cref="PreviousMigration"/>, because <c>ApplicationUser.Provider</c>'s field
/// initialiser was the only writer of that column and nothing in <c>src/</c> ever assigned
/// <c>ProviderUserId</c> (measured at <c>d90414f3</c>, the ground ADR 0017's amendment records).
/// That writer is retired by this very PR, and the pin that it produces the shape no longer is the
/// migration under test: after <c>Up</c> the columns do not exist, so no writer can reach them.
/// </para>
///
/// <para>
/// Own container, one journey method — the <c>AddTermsAcceptanceToJobSeekerMigrationTests</c> form:
/// xunit news a class instance per test method, so one method is one container. The database is
/// provisioned by <see cref="TestDatabaseProvisioner"/> with <c>includeIdentitySchema</c>, which is
/// what lets <see cref="AppIdentityDbContext"/>'s migrations run as the role production migrates
/// with rather than as the container's superuser.
/// </para>
/// </summary>
public sealed class DropAuthProviderColumnsMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20260917195454_DropAuthProviderColumns";
    private const string PreviousMigration = "20260703160805_DropRefreshTokens";

    private const string ProviderColumn = "provider";
    private const string ProviderUserIdColumn = "provider_user_id";
    private const string ProviderIndex = "ix_asp_net_users_provider_provider_user_id";

    // Ordinal order, because it doubles as the expected set of an OrderBy(StringComparer.Ordinal).
    private static readonly string[] DroppedColumns = [ProviderColumn, ProviderUserIdColumn];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private string _appConnectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        _appConnectionString = await TestDatabaseProvisioner
            .ProvisionAndGetAppConnectionStringAsync(
                _postgres.GetConnectionString(),
                includeIdentitySchema: true);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    private AppIdentityDbContext NewIdentityContext() =>
        new(MigrationsOptionsFactory.BuildIdentityOptions(_appConnectionString));

    private sealed record ColumnShape(string DataType, bool IsNullable, int? MaxLength);

    private async Task<Dictionary<string, ColumnShape>> ReadDroppedColumnsAsync(CancellationToken ct)
    {
        var columns = new Dictionary<string, ColumnShape>(StringComparer.Ordinal);

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT column_name, data_type, is_nullable, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'identity' AND table_name = 'AspNetUsers'
              AND column_name = ANY(@names)
            """,
            conn);
        cmd.Parameters.AddWithValue("names", DroppedColumns);

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

    /// <summary>
    /// The index as Postgres holds it, not as the model declares it. <c>indexdef</c> comes back so
    /// the partial filter and the UNIQUE-ness are read rather than assumed — a <c>Down</c> that
    /// recreated the index without its <c>WHERE</c> clause would restore a constraint stricter than
    /// the one it replaced, and a plain existence check would call that a pass.
    /// </summary>
    private async Task<string?> ReadProviderIndexDefAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'identity' AND tablename = 'AspNetUsers' AND indexname = @name
            """,
            conn);
        cmd.Parameters.AddWithValue("name", ProviderIndex);

        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>
    /// A row in the shape <c>AspNetUsers</c> holds at <see cref="PreviousMigration"/> — an account
    /// that exists before 6d deploys. The NOT NULL set there is id, the four Identity flags and
    /// access_failed_count, plus provider (default <c>'Local'</c>) and created_at (default
    /// <c>now()</c>); everything else is nullable.
    /// </summary>
    private async Task<Guid> InsertPreMigrationUserAsync(string email, CancellationToken ct)
    {
        var id = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO identity."AspNetUsers"
              (id, user_name, normalized_user_name, email, normalized_email, email_confirmed,
               phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count,
               provider, provider_user_id)
            VALUES
              (@id, @email, upper(@email), @email, upper(@email), true,
               false, false, true, 0,
               'Local', NULL)
            """,
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync(ct);

        return id;
    }

    private async Task<string?> ReadUserEmailAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """SELECT email FROM identity."AspNetUsers" WHERE id = @id""",
            conn);
        cmd.Parameters.AddWithValue("id", id);

        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    [Fact]
    public async Task DropAuthProviderColumns_RemovesBothColumnsAndTheFilteredIndex_KeepsAPreExistingUser_AndReverses()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewIdentityContext();

        var assembly = db.Database.GetMigrations().ToList();
        assembly.ShouldContain(ThisMigration);
        assembly.ShouldContain(PreviousMigration);

        // --- 1. Head: neither column is in the catalog, and neither is the index.
        await db.Database.MigrateAsync(ct);

        (await ReadDroppedColumnsAsync(ct)).ShouldBeEmpty();
        (await ReadProviderIndexDefAsync(ct)).ShouldBeNull();

        // --- 2. Rollback to the migration before this one: the Down restores both columns with
        // their declared shapes, and the index with its partial filter intact.
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);

        var atPrevious = await ReadDroppedColumnsAsync(ct);
        atPrevious.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(DroppedColumns);

        atPrevious[ProviderColumn].DataType.ShouldBe("character varying");
        atPrevious[ProviderColumn].IsNullable.ShouldBeFalse();
        atPrevious[ProviderColumn].MaxLength.ShouldBe(20);

        atPrevious[ProviderUserIdColumn].DataType.ShouldBe("character varying");
        atPrevious[ProviderUserIdColumn].IsNullable.ShouldBeTrue();
        atPrevious[ProviderUserIdColumn].MaxLength.ShouldBe(255);

        var restoredIndex = await ReadProviderIndexDefAsync(ct);
        restoredIndex.ShouldNotBeNull();
        restoredIndex.ShouldContain("UNIQUE INDEX");
        restoredIndex.ShouldContain("WHERE (provider_user_id IS NOT NULL)");

        // --- 2b. An account that exists before the migration — the deploy's case, not an empty table.
        var email = $"pre-6d-{Guid.NewGuid():N}@example.com";
        var legacyId = await InsertPreMigrationUserAsync(email, ct);

        // --- 3. Forward again, on the populated table: the shape the deploy runs.
        await db.Database.MigrateAsync(ct);

        (await ReadDroppedColumnsAsync(ct)).ShouldBeEmpty();
        (await ReadProviderIndexDefAsync(ct)).ShouldBeNull();
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();

        // The EFFECT on that account, not the columns' absence: the row is still there, with its
        // identifying data. DROP COLUMN narrows a table — a statement that reached the table, or a
        // cascade off the dropped unique index, would take the account with it, and every
        // catalog read above would still pass.
        (await ReadUserEmailAsync(legacyId, ct)).ShouldBe(email);
    }
}
