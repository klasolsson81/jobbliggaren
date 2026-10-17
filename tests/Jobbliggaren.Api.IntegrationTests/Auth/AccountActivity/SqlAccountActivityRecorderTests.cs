using System.Data.Common;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Infrastructure.Auth.Activity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Admin.AdminAccountsKit;

namespace Jobbliggaren.Api.IntegrationTests.Auth.AccountActivity;

/// <summary>
/// #2161 (ADR 0172) — <see cref="SqlAccountActivityRecorder"/> against the migrated schema. Every account is opened by
/// the production registrar, and every row is written by the recorder itself, each instance standing for one Api
/// process: its own claims, its own context, and a clock the test moves.
/// </summary>
[Collection("Api")]
public sealed class SqlAccountActivityRecorderTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 17, 9, 41, 37, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset MinuteOf(DateTimeOffset instant) => AccountActivityClaims.MinuteOf(instant);

    private sealed record Row(DateTimeOffset? LastLoginAt, DateTimeOffset LastActiveAt);

    /// <summary>One Api process's recorder: its own claims and context, so its statements can be counted.</summary>
    private sealed class Process : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly AppDbContext _db;

        public Process(ApiFactory factory, DateTimeOffset now, string? connectionString = null)
        {
            _scope = factory.Services.CreateAsyncScope();
            var cs = connectionString
                ?? _scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
            _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(cs)
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
                .AddInterceptors(Statements)
                .Options);
            Clock.UtcNow = now;
            _loggers = new LoggerFactory([Logs], new LoggerFilterOptions { MinLevel = LogLevel.Trace });
            Recorder = new SqlAccountActivityRecorder(
                _db, new AccountActivityClaims(), Clock, _loggers.CreateLogger<SqlAccountActivityRecorder>());
        }

        private readonly LoggerFactory _loggers;

        public MutableFakeDateTimeProvider Clock { get; } = new();

        public CapturingLoggerProvider Logs { get; } = new();

        public StatementCounter Statements { get; } = new();

        public SqlAccountActivityRecorder Recorder { get; }

        public async ValueTask DisposeAsync()
        {
            _loggers.Dispose();
            await _db.DisposeAsync();
            await _scope.DisposeAsync();
        }
    }

    /// <summary>Counts the statements that reach <c>account_activity</c>; ExecuteSqlAsync runs them as non-queries.</summary>
    private sealed class StatementCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("account_activity", StringComparison.Ordinal))
                Interlocked.Increment(ref _count);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private string ConnectionString()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
    }

    private async Task<Row?> RowOfAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT a.last_login_at, a.last_active_at
            FROM account_activity a JOIN job_seekers js ON js.id = a.job_seeker_id
            WHERE js.user_id = @user_id
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        if (!await reader.ReadAsync(Ct))
            return null;
        return new Row(
            await reader.IsDBNullAsync(0, Ct) ? null : reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async Task<long> RowCountAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM account_activity", connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private Task<Guid> OpenAsync(string label) => OpenActiveAsync(factory, Address(NewToken(), label), Ct);

    private static void ShouldHaveLoggedNothingAboveDebug(Process process) =>
        process.Logs.Logs.Where(log => log.Level > LogLevel.Debug).ShouldBeEmpty();

    [Fact]
    public async Task A_request_creates_the_record_at_its_minute_with_no_login()
    {
        var userId = await OpenAsync("request");
        await using var process = new Process(factory, Morning);

        await process.Recorder.RecordRequestAsync(userId, Ct);

        (await RowOfAsync(userId)).ShouldBe(new Row(null, MinuteOf(Morning)));
        ShouldHaveLoggedNothingAboveDebug(process);
    }

    [Fact]
    public async Task A_login_records_both_minutes_and_a_later_minute_moves_only_the_activity()
    {
        var userId = await OpenAsync("login");
        await using var process = new Process(factory, Morning);

        await process.Recorder.RecordLoginAsync(userId, Ct);
        process.Clock.UtcNow = Morning.AddMinutes(3);
        await process.Recorder.RecordRequestAsync(userId, Ct);

        (await RowOfAsync(userId)).ShouldBe(new Row(MinuteOf(Morning), MinuteOf(Morning.AddMinutes(3))));
    }

    [Fact]
    public async Task One_process_sends_one_statement_per_account_and_minute_and_a_login_covers_its_own_minute()
    {
        var userId = await OpenAsync("throttle");
        await using var process = new Process(factory, Morning);

        await process.Recorder.RecordRequestAsync(userId, Ct);
        foreach (var seconds in new[] { 5, 15, 22 })
        {
            process.Clock.UtcNow = Morning.AddSeconds(seconds);
            await process.Recorder.RecordRequestAsync(userId, Ct);
        }
        process.Statements.Count.ShouldBe(1);

        process.Clock.UtcNow = Morning.AddMinutes(1);
        await process.Recorder.RecordLoginAsync(userId, Ct);
        process.Clock.UtcNow = Morning.AddMinutes(1).AddSeconds(10);
        await process.Recorder.RecordRequestAsync(userId, Ct);
        process.Statements.Count.ShouldBe(2);

        process.Clock.UtcNow = Morning.AddMinutes(2);
        await process.Recorder.RecordRequestAsync(userId, Ct);
        process.Statements.Count.ShouldBe(3);
        (await RowOfAsync(userId)).ShouldBe(new Row(MinuteOf(Morning.AddMinutes(1)), MinuteOf(Morning.AddMinutes(2))));
    }

    [Fact]
    public async Task An_earlier_minute_from_another_process_never_moves_a_value_back()
    {
        var userId = await OpenAsync("monotonic");
        await using (var later = new Process(factory, Morning.AddMinutes(5)))
            await later.Recorder.RecordLoginAsync(userId, Ct);

        await using (var earlier = new Process(factory, Morning))
        {
            await earlier.Recorder.RecordRequestAsync(userId, Ct);
            await earlier.Recorder.RecordLoginAsync(userId, Ct);
            ShouldHaveLoggedNothingAboveDebug(earlier);
        }

        (await RowOfAsync(userId)).ShouldBe(new Row(MinuteOf(Morning.AddMinutes(5)), MinuteOf(Morning.AddMinutes(5))));
    }

    [Fact]
    public async Task Concurrent_writers_converge_on_the_latest_minutes()
    {
        var userId = await OpenAsync("concurrent");
        var minutes = Enumerable.Range(0, 12).Select(i => Morning.AddMinutes(i)).OrderBy(_ => Random.Shared.Next()).ToList();
        var processes = minutes.Select(minute => new Process(factory, minute)).ToList();
        try
        {
            await Task.WhenAll(processes.Select((process, i) => i % 3 == 0
                ? process.Recorder.RecordLoginAsync(userId, Ct)
                : process.Recorder.RecordRequestAsync(userId, Ct)));

            var latestLogin = processes.Where((_, i) => i % 3 == 0).Max(process => process.Clock.UtcNow);
            (await RowOfAsync(userId)).ShouldBe(new Row(MinuteOf(latestLogin), MinuteOf(minutes.Max())));
            foreach (var process in processes)
                ShouldHaveLoggedNothingAboveDebug(process);
        }
        finally
        {
            foreach (var process in processes)
                await process.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_account_without_a_profile_gets_no_record_and_no_warning()
    {
        // The historical shape AdminAccountsKit.CreateWithoutProfileAsync documents; the write keys through the profile.
        var userId = await CreateWithoutProfileAsync(factory, Address(NewToken(), "profileless"), Ct);
        var before = await RowCountAsync();
        await using var process = new Process(factory, Morning);

        await process.Recorder.RecordLoginAsync(userId, Ct);
        process.Clock.UtcNow = Morning.AddMinutes(1);
        await process.Recorder.RecordRequestAsync(userId, Ct);

        process.Statements.Count.ShouldBe(2);
        (await RowCountAsync()).ShouldBe(before);
        ShouldHaveLoggedNothingAboveDebug(process);
    }

    [Fact]
    public async Task A_database_outage_is_logged_without_the_address_and_never_thrown()
    {
        var email = Address(NewToken(), "outage");
        var userId = await OpenActiveAsync(factory, email, Ct);
        // A database that refuses connections: nothing listens on port 1.
        var refusing = new NpgsqlConnectionStringBuilder(ConnectionString()) { Port = 1, Timeout = 1 }.ConnectionString;
        await using var process = new Process(factory, Morning, refusing);

        await process.Recorder.RecordLoginAsync(userId, Ct);
        process.Clock.UtcNow = Morning.AddMinutes(1);
        await process.Recorder.RecordRequestAsync(userId, Ct);

        var failures = process.Logs.Logs.Where(log => log.EventId.Id == 2701).ToList();
        failures.Count.ShouldBe(2);
        failures.ShouldAllBe(log => log.Level == LogLevel.Warning);
        failures.ShouldAllBe(log => log.Message.Contains(userId.ToString(), StringComparison.Ordinal));
        process.Logs.Logs.ShouldAllBe(log => !log.AllText.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_write_held_past_its_budget_is_abandoned_logged_and_never_thrown()
    {
        var userId = await OpenAsync("budget");
        // A migration or a VACUUM FULL holds the table: ACCESS EXCLUSIVE until its transaction ends.
        await using var holder = new NpgsqlConnection(ConnectionString());
        await holder.OpenAsync(Ct);
        await using var transaction = await holder.BeginTransactionAsync(Ct);
        await using (var lockTable = new NpgsqlCommand("LOCK TABLE account_activity IN ACCESS EXCLUSIVE MODE", holder, transaction))
            await lockTable.ExecuteNonQueryAsync(Ct);
        await using var process = new Process(factory, Morning);
        var started = TimeProvider.System.GetTimestamp();

        await process.Recorder.RecordRequestAsync(userId, Ct);

        var waited = TimeProvider.System.GetElapsedTime(started);
        await transaction.RollbackAsync(Ct);
        waited.ShouldBeGreaterThanOrEqualTo(SqlAccountActivityRecorder.WriteBudget - TimeSpan.FromMilliseconds(100));
        waited.ShouldBeLessThan(SqlAccountActivityRecorder.WriteBudget + TimeSpan.FromSeconds(5));
        process.Logs.Logs.ShouldContain(log => log.EventId.Id == 2703 && log.Level == LogLevel.Warning);
        (await RowOfAsync(userId)).ShouldBeNull();
    }

    [Fact]
    public async Task A_profile_erased_while_its_write_waits_is_an_expected_outcome_below_warning()
    {
        var userId = await OpenAsync("erased-meanwhile");
        // The hard delete's transaction has removed the profile row but not yet committed: the write reads the row,
        // waits on its key-share lock, and finds it gone at the commit.
        await using var deleter = new NpgsqlConnection(ConnectionString());
        await deleter.OpenAsync(Ct);
        await using var transaction = await deleter.BeginTransactionAsync(Ct);
        await using (var delete = new NpgsqlCommand("DELETE FROM job_seekers WHERE user_id = @user_id", deleter, transaction))
        {
            delete.Parameters.AddWithValue("user_id", userId);
            (await delete.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
        }
        await using var process = new Process(factory, Morning);

        var write = process.Recorder.RecordRequestAsync(userId, Ct);
        await Task.Delay(200, Ct);
        await transaction.CommitAsync(Ct);
        await write;

        process.Logs.Logs.ShouldContain(log => log.EventId.Id == 2702 && log.Level == LogLevel.Debug);
        process.Logs.Logs.ShouldNotContain(log => log.Level >= LogLevel.Warning);
        (await RowOfAsync(userId)).ShouldBeNull();
    }
}
