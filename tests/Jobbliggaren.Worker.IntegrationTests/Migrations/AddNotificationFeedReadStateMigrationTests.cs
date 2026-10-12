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
/// #2130 PR 2 — the bell's read flag is additive: both source tables gain <c>read_in_notification_feed boolean NOT NULL
/// DEFAULT false</c> with every existing row reading <c>false</c> (today's meaning: not individually read), the
/// <c>followed_company_ad_hits</c> index swap lands as one named step, and Down puts the schema back, across Up, Down and
/// a second Up. Run as <c>jobbliggaren_app</c>, the role production migrates with.
///
/// <para>
/// The rows seeded at the previous schema are the shape the producers write and nothing else:
/// <c>BackgroundMatchingJob</c> inserts a <c>Pending</c> match with <c>created_at</c> stamped by the clock, and
/// <c>CompanyWatchScanJob</c> a <c>Pending</c> hit with no <c>seen_at</c>; no code path writes the new column
/// before this migration, so there is no earlier state to model. They are inserted with SQL because the current
/// model already maps the column, which the previous schema does not have.
/// </para>
/// </summary>
public sealed class AddNotificationFeedReadStateMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261012042409_AddNotificationFeedReadState";
    private const string PreviousMigration = "20261011102453_AddNotificationMailPauses";
    private const string OldIndex = "ix_followed_company_ad_hits_user_id";
    private const string NewIndex = "ix_followed_company_ad_hits_user_created_at";
    private static readonly DateTimeOffset Created = new(2026, 10, 11, 9, 30, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private string _appConnectionString = string.Empty;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(Ct);
        _appConnectionString = await TestDatabaseProvisioner.ProvisionAndGetAppConnectionStringAsync(
            _postgres.GetConnectionString(), ct: Ct);
        await using var superuser = new NpgsqlConnection(_postgres.GetConnectionString());
        await superuser.OpenAsync(Ct);
        await using var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm;", superuser);
        await extension.ExecuteNonQueryAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task MigrateAsync_ShouldAddTheFlagDefaultFalseAndSwapTheIndexLeavingEveryColumnAsItWas_AcrossUpDownAndReUp()
    {
        await using var db = NewAppContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await FlagColumnsAsync()).ShouldBeEmpty();
        (await HitIndexDefinitionAsync(OldIndex)).ShouldNotBeNull();
        (await HitIndexDefinitionAsync(NewIndex)).ShouldBeNull();
        var userId = Guid.NewGuid();
        await SeedProducerRowsAsync(userId);
        var previousSchema = await ReadSchemaWithoutTheFlagAsync();

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync(userId);
        (await ReadSchemaWithoutTheFlagAsync()).ShouldBe(previousSchema);

        // The model reads and writes the column as the app role: a mark persists, and Down below drops it with the row intact.
        await using (var writer = NewAppContext())
        {
            var match = await writer.UserJobAdMatches.AsNoTracking().SingleAsync(m => m.UserId == userId, Ct);
            match.ReadInNotificationFeed.ShouldBeFalse();
            // A match is marked by one set-based statement, as production marks it.
            await writer.UserJobAdMatches.Where(m => m.UserId == userId)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.ReadInNotificationFeed, true), Ct);
            var hit = await writer.FollowedCompanyAdHits.SingleAsync(h => h.UserId == userId, Ct);
            hit.ReadInNotificationFeed.ShouldBeFalse();
            hit.MarkReadInNotificationFeed();
            await writer.SaveChangesAsync(Ct);
        }
        await using (var read = NewAppContext())
        {
            (await read.UserJobAdMatches.AsNoTracking().SingleAsync(m => m.UserId == userId, Ct))
                .ReadInNotificationFeed.ShouldBeTrue();
            var hit = await read.FollowedCompanyAdHits.AsNoTracking().SingleAsync(h => h.UserId == userId, Ct);
            hit.ReadInNotificationFeed.ShouldBeTrue();
            hit.SeenAt.ShouldBeNull("the bell's read mark is not the e-mail dedup stamp");
        }

        await migrator.MigrateAsync(PreviousMigration, Ct);

        (await FlagColumnsAsync()).ShouldBeEmpty();
        (await HitIndexDefinitionAsync(OldIndex)).ShouldNotBeNull();
        (await HitIndexDefinitionAsync(NewIndex)).ShouldBeNull();
        (await ReadSchemaWithoutTheFlagAsync()).ShouldBe(previousSchema);
        (await CountAsync("user_job_ad_matches", userId)).ShouldBe(1, "Down keeps the rows and drops only the flag");
        (await CountAsync("followed_company_ad_hits", userId)).ShouldBe(1);

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync(userId);
    }

    private async Task AssertUpAsync(Guid userId)
    {
        (await FlagColumnsAsync()).ShouldBe([
            "followed_company_ad_hits|boolean|NO|false",
            "user_job_ad_matches|boolean|NO|false",
        ]);

        // The DEFAULT is the whole backfill: the pre-existing rows read false, or, after Down and a second Up,
        // false again (the flag a test set above was dropped with its column).
        (await FlagValuesAsync("user_job_ad_matches", userId)).ShouldBe([false]);
        (await FlagValuesAsync("followed_company_ad_hits", userId)).ShouldBe([false]);

        (await HitIndexDefinitionAsync(OldIndex)).ShouldBeNull("the plain user_id index is replaced, not joined");
        var swapped = await HitIndexDefinitionAsync(NewIndex);
        swapped.ShouldNotBeNull();
        swapped!.ShouldContain("(user_id, created_at DESC)");
        swapped.ShouldNotContain("WHERE", customMessage: "a full B-tree, as the index it replaces");
    }

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private async Task SeedProducerRowsAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var match = new NpgsqlCommand(
            """
            INSERT INTO user_job_ad_matches (id, user_id, job_ad_id, grade, notification_status, created_at)
            VALUES (gen_random_uuid(), @user, gen_random_uuid(), 'Strong', 'Pending', @created)
            """, connection);
        match.Parameters.AddWithValue("user", userId);
        match.Parameters.AddWithValue("created", Created);
        await match.ExecuteNonQueryAsync(Ct);
        await using var hit = new NpgsqlCommand(
            """
            INSERT INTO followed_company_ad_hits (id, user_id, job_ad_id, company_watch_id, notification_status, created_at)
            VALUES (gen_random_uuid(), @user, gen_random_uuid(), gen_random_uuid(), 'Pending', @created)
            """, connection);
        hit.Parameters.AddWithValue("user", userId);
        hit.Parameters.AddWithValue("created", Created);
        await hit.ExecuteNonQueryAsync(Ct);
    }

    private async Task<List<string>> FlagColumnsAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, 'none')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name = 'read_in_notification_feed'
            ORDER BY table_name
            """, connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            columns.Add(reader.GetString(0));
        return columns;
    }

    private async Task<List<bool>> FlagValuesAsync(string table, Guid userId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            $"SELECT read_in_notification_feed FROM {table} WHERE user_id = @user", connection);
        command.Parameters.AddWithValue("user", userId);
        var values = new List<bool>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            values.Add(reader.GetBoolean(0));
        return values;
    }

    private async Task<long> CountAsync(string table, Guid userId)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE user_id = @user", connection);
        command.Parameters.AddWithValue("user", userId);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<string?> HitIndexDefinitionAsync(string indexName)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'followed_company_ad_hits' AND indexname = @name",
            connection);
        command.Parameters.AddWithValue("name", indexName);
        return (string?)await command.ExecuteScalarAsync(Ct);
    }

    private async Task<List<string>> ReadSchemaWithoutTheFlagAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name || '|' || column_name || '|' || data_type || '|' || is_nullable
                   || '|' || coalesce(character_maximum_length::text, '') || '|' || coalesce(column_default, '')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name <> 'read_in_notification_feed'
            ORDER BY table_name, column_name
            """, connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            columns.Add(reader.GetString(0));
        return columns;
    }
}
