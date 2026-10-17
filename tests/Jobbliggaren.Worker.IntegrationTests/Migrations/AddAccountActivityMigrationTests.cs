using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth.Activity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// #2161 (ADR 0172) — <c>AddAccountActivity</c> is additive: one new table, keyed by the profile and erased with it,
/// and every other table's columns exactly as before, across Up, Down and a second Up. Run as
/// <c>jobbliggaren_app</c>, the role production migrates with. The profile is registered by
/// <see cref="JobSeeker.Register"/> and the record is written by the production recorder.
/// </summary>
public sealed class AddAccountActivityMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261017151355_AddAccountActivity";
    private const string PreviousMigration = "20261012085507_AddNotificationFeedDismissal";
    private static readonly DateTimeOffset Registered = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
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
    public async Task MigrateAsync_ShouldCreateTheRecordKeyedAndErasedByTheProfileLeavingEveryOtherTableAsItWas_AcrossUpDownAndReUp()
    {
        await using var db = NewAppContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await ColumnsAsync()).ShouldBeEmpty();
        var previousSchema = await ReadSchemaWithoutTheRecordAsync();
        var existing = await RegisterProfileAsync(Guid.NewGuid());

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync();
        (await ReadSchemaWithoutTheRecordAsync()).ShouldBe(previousSchema);
        (await CountAsync(existing)).ShouldBe(0, "Up fills nothing in: recording starts with the release");

        // The app role writes the record through the production recorder, and the cascade takes it with the profile.
        var userId = Guid.NewGuid();
        var profile = await RegisterProfileAsync(userId);
        await RecordLoginAsync(userId);
        (await CountAsync(profile)).ShouldBe(1);
        await using (var deleter = NewAppContext())
        {
            deleter.JobSeekers.Remove(await deleter.JobSeekers.SingleAsync(js => js.Id == profile, Ct));
            await deleter.SaveChangesAsync(Ct);
        }
        (await CountAsync(profile)).ShouldBe(0);

        await migrator.MigrateAsync(PreviousMigration, Ct);

        (await ColumnsAsync()).ShouldBeEmpty();
        (await ReadSchemaWithoutTheRecordAsync()).ShouldBe(previousSchema);

        await migrator.MigrateAsync(ThisMigration, Ct);

        await AssertUpAsync();
        (await ReadSchemaWithoutTheRecordAsync()).ShouldBe(previousSchema);
        (await CountAsync(existing)).ShouldBe(0);
    }

    private async Task AssertUpAsync()
    {
        (await ColumnsAsync()).ShouldBe([
            "job_seeker_id|uuid|NO",
            "last_active_at|timestamp with time zone|NO",
            "last_login_at|timestamp with time zone|YES",
        ]);
        (await ConstraintsAsync()).ShouldBe([
            "fk_account_activity_job_seekers_job_seeker_id|f|job_seekers|c",
            "pk_account_activity|p||",
        ]);
        (await IndexesAsync()).ShouldBe(["pk_account_activity"], "the primary key is the record's only index");
    }

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private async Task<JobSeekerId> RegisterProfileAsync(Guid userId)
    {
        var clock = new FixedClock(Registered);
        var profile = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        await using var writer = NewAppContext();
        writer.JobSeekers.Add(profile);
        await writer.SaveChangesAsync(Ct);
        return profile.Id;
    }

    private async Task RecordLoginAsync(Guid userId)
    {
        await using var writer = NewAppContext();
        var recorder = new SqlAccountActivityRecorder(
            writer, new AccountActivityClaims(), new FixedClock(Registered.AddMinutes(5)),
            NullLogger<SqlAccountActivityRecorder>.Instance);
        await recorder.RecordLoginAsync(userId, Ct);
    }

    private async Task<int> CountAsync(JobSeekerId profile)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::int FROM account_activity WHERE job_seeker_id = @id", connection);
        command.Parameters.AddWithValue("id", profile.Value);
        return (int)(await command.ExecuteScalarAsync(Ct))!;
    }

    private Task<List<string>> ColumnsAsync() => ReadStringsAsync(
        """
        SELECT column_name || '|' || data_type || '|' || is_nullable
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'account_activity'
        ORDER BY column_name
        """);

    private Task<List<string>> ConstraintsAsync() => ReadStringsAsync(
        """
        SELECT c.conname || '|' || c.contype::text || '|' || coalesce(r.relname, '') || '|'
            || CASE WHEN c.contype = 'f' THEN c.confdeltype::text ELSE '' END
        FROM pg_constraint c
        LEFT JOIN pg_class r ON r.oid = c.confrelid
        WHERE c.conrelid = 'public.account_activity'::regclass AND c.contype <> 'n'
        ORDER BY c.conname
        """);

    private Task<List<string>> IndexesAsync() => ReadStringsAsync(
        "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'account_activity' ORDER BY indexname");

    private Task<List<string>> ReadSchemaWithoutTheRecordAsync() => ReadStringsAsync(
        """
        SELECT table_name || '.' || column_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, '')
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name <> 'account_activity'
        ORDER BY table_name, column_name
        """);

    private async Task<List<string>> ReadStringsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            values.Add(reader.GetString(0));
        return values;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
