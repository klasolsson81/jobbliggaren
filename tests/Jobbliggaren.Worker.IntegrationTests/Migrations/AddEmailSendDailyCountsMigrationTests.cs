using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Email.Counting;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Time;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Worker.IntegrationTests.Migrations;

/// <summary>
/// #1981 PR 3 (ADR 0180) — <c>AddEmailSendDailyCounts</c> is additive: two new tables, the recording start written
/// once by the migration itself, and every other table's columns exactly as before, across Up, Down and a second Up.
/// Run as <c>jobbliggaren_app</c>, the role production migrates with.
/// </summary>
public sealed class AddEmailSendDailyCountsMigrationTests : IAsyncLifetime
{
    private const string ThisMigration = "20261023051327_AddEmailSendDailyCounts";
    private const string PreviousMigration = "20261019094325_AddJobAdApplicationUrl";
    private static readonly string[] NewTables = ["email_send_counting", "email_send_daily_counts"];
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
    public async Task MigrateAsync_ShouldCreateBothTablesAndStampTheStartOnceLeavingEveryOtherTableAsItWas_AcrossUpDownAndReUp()
    {
        await using var db = NewAppContext();
        var migrations = db.Database.GetMigrations().ToList();
        migrations.IndexOf(PreviousMigration).ShouldBe(migrations.IndexOf(ThisMigration) - 1);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);
        (await ColumnsAsync()).ShouldBeEmpty();
        var previousSchema = await ReadSchemaWithoutTheNewTablesAsync();

        var before = await DatabaseNowAsync();
        await migrator.MigrateAsync(ThisMigration, Ct);
        var after = await DatabaseNowAsync();

        await AssertUpAsync(before, after);
        (await ReadSchemaWithoutTheNewTablesAsync()).ShouldBe(previousSchema);

        await migrator.MigrateAsync(PreviousMigration, Ct);

        (await ColumnsAsync()).ShouldBeEmpty("Down drops both tables and every count with them");
        (await ReadSchemaWithoutTheNewTablesAsync()).ShouldBe(previousSchema);

        var beforeReUp = await DatabaseNowAsync();
        await migrator.MigrateAsync(ThisMigration, Ct);
        var afterReUp = await DatabaseNowAsync();

        await AssertUpAsync(beforeReUp, afterReUp);
        (await ReadSchemaWithoutTheNewTablesAsync()).ShouldBe(previousSchema);
    }

    [Fact]
    public async Task TheConstraints_ShouldRefuseAZeroCountAZeroKindOrOutcomeAndASecondStartRow_AsTheAppRole()
    {
        await using (var db = NewAppContext())
            await db.GetService<IMigrator>().MigrateAsync(ThisMigration, Ct);

        await ShouldViolateAsync(
            "INSERT INTO email_send_daily_counts (day, kind, outcome, attempts) VALUES ('2026-10-23', 5, 1, 0)", "23514");
        await ShouldViolateAsync(
            "INSERT INTO email_send_daily_counts (day, kind, outcome, attempts) VALUES ('2026-10-23', 0, 1, 1)", "23514");
        await ShouldViolateAsync(
            "INSERT INTO email_send_daily_counts (day, kind, outcome, attempts) VALUES ('2026-10-23', 5, 0, 1)", "23514");
        await ShouldViolateAsync("INSERT INTO email_send_counting (id, started_at) VALUES (2, now())", "23514");
        await ShouldViolateAsync("INSERT INTO email_send_counting (id, started_at) VALUES (1, now())", "23505");
    }

    /// <summary>
    /// The production tally writes through the app role into the migrated table: one row per date, kind and outcome,
    /// incremented in place, and it never touches the start row the migration wrote.
    /// </summary>
    [Fact]
    public async Task TheTally_ShouldIncrementOneRowPerDateKindAndOutcome_AndNeverTouchTheStart()
    {
        await using (var db = NewAppContext())
            await db.GetService<IMigrator>().MigrateAsync(ThisMigration, Ct);
        var start = (await ReadStartsAsync()).ShouldHaveSingleItem();
        var services = new ServiceCollection();
        services.AddScoped(_ => NewAppContext());
        services.AddSingleton<IDateTimeProvider>(new FixedClock(new DateTimeOffset(2026, 10, 23, 22, 30, 0, TimeSpan.Zero)));
        services.AddSingleton<ISwedishCalendar, SwedishCalendar>();
        await using var provider = services.BuildServiceProvider();
        var tally = new SqlEmailSendTally(
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SqlEmailSendTally>.Instance);

        await tally.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Accepted);
        await tally.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Accepted);
        await tally.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Refused);

        (await ReadStringsAsync(
            "SELECT day::text || '|' || kind || '|' || outcome || '|' || attempts FROM email_send_daily_counts ORDER BY outcome"))
            .ShouldBe(["2026-10-24|5|1|2", "2026-10-24|5|2|1"], "22:30Z on 23 October is the 24th in Stockholm (CEST)");
        (await ReadStartsAsync()).ShouldHaveSingleItem().ShouldBe(start);
    }

    private async Task AssertUpAsync(DateTimeOffset before, DateTimeOffset after)
    {
        (await ColumnsAsync()).ShouldBe([
            "email_send_counting.id|integer|NO",
            "email_send_counting.started_at|timestamp with time zone|NO",
            "email_send_daily_counts.attempts|integer|NO",
            "email_send_daily_counts.day|date|NO",
            "email_send_daily_counts.kind|smallint|NO",
            "email_send_daily_counts.outcome|smallint|NO",
        ]);
        (await ConstraintsAsync()).ShouldBe([
            "ck_email_send_counting_singleton|CHECK ((id = 1))",
            "ck_email_send_daily_counts_positive|CHECK (((attempts > 0) AND (kind > 0) AND (outcome > 0)))",
            "pk_email_send_counting|PRIMARY KEY (id)",
            "pk_email_send_daily_counts|PRIMARY KEY (day, kind, outcome)",
        ]);
        (await IndexesAsync()).ShouldBe(
            ["pk_email_send_counting", "pk_email_send_daily_counts"], "the primary keys are the only indexes");

        (await ReadStringsAsync("SELECT count(*)::text FROM email_send_daily_counts")).ShouldBe(["0"]);
        var starts = await ReadStartsAsync();
        starts.Count.ShouldBe(1, "Up writes exactly one recording start");
        starts[0].Id.ShouldBe(1);
        starts[0].StartedAt.ShouldBeInRange(before, after);
    }

    private AppDbContext NewAppContext() =>
        new(MigrationsOptionsFactory.BuildAppOptions(_appConnectionString));

    private async Task ShouldViolateAsync(string sql, string sqlState)
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        var ex = await Should.ThrowAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync(Ct));
        ex.SqlState.ShouldBe(sqlState);
    }

    private async Task<DateTimeOffset> DatabaseNowAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT now()", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        await reader.ReadAsync(Ct);
        return reader.GetFieldValue<DateTimeOffset>(0);
    }

    private async Task<List<(int Id, DateTimeOffset StartedAt)>> ReadStartsAsync()
    {
        await using var connection = new NpgsqlConnection(_appConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT id, started_at FROM email_send_counting", connection);
        var rows = new List<(int, DateTimeOffset)>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            rows.Add((reader.GetInt32(0), reader.GetFieldValue<DateTimeOffset>(1)));
        return rows;
    }

    private Task<List<string>> ColumnsAsync() => ReadStringsAsync(
        """
        SELECT table_name || '.' || column_name || '|' || data_type || '|' || is_nullable
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name IN ('email_send_counting', 'email_send_daily_counts')
        ORDER BY table_name, column_name
        """);

    private Task<List<string>> ConstraintsAsync() => ReadStringsAsync(
        """
        SELECT c.conname || '|' || pg_get_constraintdef(c.oid)
        FROM pg_constraint c
        WHERE c.conrelid IN ('public.email_send_counting'::regclass, 'public.email_send_daily_counts'::regclass)
          AND c.contype <> 'n'
        ORDER BY c.conname
        """);

    private Task<List<string>> IndexesAsync() => ReadStringsAsync(
        """
        SELECT indexname FROM pg_indexes
        WHERE schemaname = 'public' AND tablename IN ('email_send_counting', 'email_send_daily_counts')
        ORDER BY indexname
        """);

    private Task<List<string>> ReadSchemaWithoutTheNewTablesAsync() => ReadStringsAsync(
        $"""
        SELECT table_name || '.' || column_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, '')
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name NOT IN ('{NewTables[0]}', '{NewTables[1]}')
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
