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
/// on a POPULATED table.
///
/// <para>
/// This is a DESTRUCTIVE migration: two <c>DROP COLUMN</c>s and a <c>DROP INDEX</c> on the table
/// that holds every account. The journey is head → previous → head with an account inserted at
/// head, so both directions run on rows; everything is read out of the catalog rather than inferred
/// from the migration file. No other test in the suite has a populated identity table at migration
/// time.
/// </para>
///
/// <para>
/// Own container, one journey method — the <c>AddTermsAcceptanceToJobSeekerMigrationTests</c> form:
/// xunit news a class instance per test method, so one method is one container. The database is
/// provisioned by <see cref="TestDatabaseProvisioner"/> with <c>includeIdentitySchema</c>, which
/// runs the migrations as <c>Roles.App</c> — a stricter posture than the master credentials
/// <c>Jobbliggaren.Migrate</c>'s <c>bootstrap</c> mode applies this context with.
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

    private sealed record ColumnShape(string DataType, bool IsNullable, int? MaxLength, string? Default);

    private async Task<Dictionary<string, ColumnShape>> ReadDroppedColumnsAsync(CancellationToken ct)
    {
        var columns = new Dictionary<string, ColumnShape>(StringComparer.Ordinal);

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT column_name, data_type, is_nullable, character_maximum_length, column_default
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
                await reader.IsDBNullAsync(3, ct) ? null : reader.GetInt32(3),
                await reader.IsDBNullAsync(4, ct) ? null : reader.GetString(4));
        }

        return columns;
    }

    /// <summary>
    /// The index as Postgres holds it, not as the model declares it. <c>indexdef</c> comes back so
    /// the partial filter and the UNIQUE-ness are read rather than assumed — a <c>Down</c> that
    /// recreated the index without its <c>WHERE</c> clause would restore a constraint stricter than
    /// the one it replaced, and a plain existence check would call that a pass. Reading it at head
    /// is separately worth the query: <c>DropIndex</c> names the index as a string.
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
    /// An account in the shape <c>AspNetUsers</c> holds at head, written with only the columns that
    /// exist there. The NOT NULL set is id, the four Identity flags and access_failed_count, plus
    /// created_at (store default <c>now()</c>); everything else is nullable.
    /// </summary>
    private async Task<Guid> InsertUserAsync(string email, CancellationToken ct)
    {
        var id = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO identity."AspNetUsers"
              (id, user_name, normalized_user_name, email, normalized_email, email_confirmed,
               phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
            VALUES
              (@id, @email, upper(@email), @email, upper(@email), true,
               false, false, true, 0)
            """,
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync(ct);

        return id;
    }

    /// <summary>
    /// What the rolled-back columns hold for an account that existed across the <c>Down</c>. The
    /// value comes from the restored column default, so reading it is what tells a default that was
    /// dropped or changed apart from one that was restored.
    /// </summary>
    private async Task<(string? Provider, bool ProviderUserIdIsNull)> ReadRestoredProviderAsync(
        Guid id, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """SELECT provider, provider_user_id IS NULL FROM identity."AspNetUsers" WHERE id = @id""",
            conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).ShouldBeTrue("the account must survive the rollback");
        return (await reader.IsDBNullAsync(0, ct) ? null : reader.GetString(0), reader.GetBoolean(1));
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

        // --- 2. An account, inserted here so BOTH directions below run on a populated table. A
        // rollback meets rows, and that is the half an empty-table journey cannot grade.
        var email = $"jbl-6d-{Guid.NewGuid():N}@example.com";
        var accountId = await InsertUserAsync(email, ct);

        // --- 3. Rollback: the Down restores both columns with their declared shapes, the index with
        // its partial filter, and the default that lets a NOT NULL column land on existing rows.
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);

        var atPrevious = await ReadDroppedColumnsAsync(ct);
        atPrevious.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(DroppedColumns);

        atPrevious[ProviderColumn].DataType.ShouldBe("character varying");
        atPrevious[ProviderColumn].IsNullable.ShouldBeFalse();
        atPrevious[ProviderColumn].MaxLength.ShouldBe(20);
        atPrevious[ProviderColumn].Default.ShouldBe("'Local'::character varying");

        atPrevious[ProviderUserIdColumn].DataType.ShouldBe("character varying");
        atPrevious[ProviderUserIdColumn].IsNullable.ShouldBeTrue();
        atPrevious[ProviderUserIdColumn].MaxLength.ShouldBe(255);
        atPrevious[ProviderUserIdColumn].Default.ShouldBeNull();

        var restoredIndex = await ReadProviderIndexDefAsync(ct);
        restoredIndex.ShouldNotBeNull();
        restoredIndex.ShouldContain("UNIQUE INDEX");
        restoredIndex.ShouldContain("WHERE (provider_user_id IS NOT NULL)");

        // The rolled-back account carries what the restored default writes. A Down that dropped the
        // default fails the step outright; one that changed it lands here instead.
        (await ReadRestoredProviderAsync(accountId, ct)).ShouldBe(("Local", true));

        // --- 4. Forward again, on the populated table: the shape the deploy runs.
        await db.Database.MigrateAsync(ct);

        (await ReadDroppedColumnsAsync(ct)).ShouldBeEmpty();
        (await ReadProviderIndexDefAsync(ct)).ShouldBeNull();
        (await db.Database.GetPendingMigrationsAsync(ct)).ShouldBeEmpty();

        // The EFFECT on that account, not the columns' absence: the row is still there, with its
        // identifying data. DROP COLUMN narrows a table — a statement that reached the table instead
        // would take the account with it, and every catalog read above would still pass.
        (await ReadUserEmailAsync(accountId, ct)).ShouldBe(email);
    }
}
