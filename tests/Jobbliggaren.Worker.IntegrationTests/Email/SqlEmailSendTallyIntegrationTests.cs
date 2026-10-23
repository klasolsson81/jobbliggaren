using System.Diagnostics;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Email.Counting;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Time;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Email;

/// <summary>
/// #1981 PR 3 (ADR 0180) — the PostgreSQL tally against the migrated schema. It counts through the Worker's own
/// composition; concurrent attempts on one row all count; the day is the Swedish civil date of the server clock; and a
/// write held up by a row lock is bounded by the budget and leaves one Warning. Rows are written by the production
/// tally under a fixed clock (AGENTS.md §5 Tests), on 2031 dates no other test uses, and removed afterwards.
/// </summary>
[Collection("Worker")]
public sealed class SqlEmailSendTallyIntegrationTests(WorkerTestFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan WithinTheBudget = EmailSendTally.Budget + TimeSpan.FromSeconds(2);
    private readonly ListLogger<SqlEmailSendTally> _logger = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM email_send_daily_counts WHERE day >= DATE '2031-01-01'", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task TheWorkersOwnComposition_SuppliesEverythingTheTallyResolves_AndCountsTheAttempt()
    {
        var calendar = fixture.Services.GetRequiredService<ISwedishCalendar>();
        var clock = fixture.Services.GetRequiredService<IDateTimeProvider>();
        var dayBefore = calendar.DayOf(clock.UtcNow);
        var before = await AttemptsAsync(dayBefore, EmailKind.FeedbackReply, EmailSendOutcome.Other)
            + await AttemptsAsync(dayBefore.AddDays(1), EmailKind.FeedbackReply, EmailSendOutcome.Other);
        var sut = new SqlEmailSendTally(fixture.Services.GetRequiredService<IServiceScopeFactory>(), _logger);

        await sut.RecordAsync(EmailKind.FeedbackReply, EmailSendOutcome.Other);

        _logger.Records.ShouldBeEmpty();
        var after = await AttemptsAsync(dayBefore, EmailKind.FeedbackReply, EmailSendOutcome.Other)
            + await AttemptsAsync(dayBefore.AddDays(1), EmailKind.FeedbackReply, EmailSendOutcome.Other);
        after.ShouldBe(before + 1, "a write that midnight split still lands on one of the two dates");
        await ExecuteAsync(
            "DELETE FROM email_send_daily_counts WHERE day IN (@day, @next) AND kind = 7 AND outcome = 7",
            ("day", dayBefore), ("next", dayBefore.AddDays(1)));
    }

    [Fact]
    public async Task ConcurrentAttemptsOnOneRow_AllCount()
    {
        const int Concurrent = 10;
        await using var services = await CompositionAsync(new DateTimeOffset(2031, 3, 10, 12, 0, 0, TimeSpan.Zero));
        await FillThePoolAsync(Concurrent);
        var sut = new SqlEmailSendTally(services.GetRequiredService<IServiceScopeFactory>(), _logger);
        await sut.RecordAsync(EmailKind.FeedbackReceivedNotification, EmailSendOutcome.Accepted);

        await Task.WhenAll(Enumerable.Range(0, Concurrent).Select(_ =>
            sut.RecordAsync(EmailKind.FeedbackReceivedNotification, EmailSendOutcome.Accepted).AsTask()));

        _logger.Records.ShouldBeEmpty("every write is confirmed within the budget");
        (await AttemptsAsync(new DateOnly(2031, 3, 10), EmailKind.FeedbackReceivedNotification, EmailSendOutcome.Accepted))
            .ShouldBe(11);
    }

    [Theory]
    [InlineData("2031-01-15T22:59:59Z", "2031-01-15")]
    [InlineData("2031-01-15T23:00:00Z", "2031-01-16")]
    [InlineData("2031-07-15T21:59:59Z", "2031-07-15")]
    [InlineData("2031-07-15T22:00:00Z", "2031-07-16")]
    public async Task TheDay_IsTheSwedishCivilDateOfTheServerClock(string utc, string swedishDate)
    {
        var day = DateOnly.Parse(swedishDate, System.Globalization.CultureInfo.InvariantCulture);
        await using var services = await CompositionAsync(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture));
        var sut = new SqlEmailSendTally(services.GetRequiredService<IServiceScopeFactory>(), _logger);
        var before = await AttemptsAsync(day, EmailKind.EmailChangedNotification, EmailSendOutcome.Refused);

        await sut.RecordAsync(EmailKind.EmailChangedNotification, EmailSendOutcome.Refused);

        (await AttemptsAsync(day, EmailKind.EmailChangedNotification, EmailSendOutcome.Refused)).ShouldBe(before + 1);
    }

    [Fact]
    public async Task AWriteHeldUpByARowLock_ReturnsWithinTheBudget_AndLeavesOneWarning()
    {
        await using var services = await CompositionAsync(new DateTimeOffset(2031, 5, 5, 9, 0, 0, TimeSpan.Zero));
        var sut = new SqlEmailSendTally(services.GetRequiredService<IServiceScopeFactory>(), _logger);
        await sut.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Timeout);

        await using var locker = new NpgsqlConnection(fixture.ConnectionString);
        await locker.OpenAsync(Ct);
        await using var transaction = await locker.BeginTransactionAsync(Ct);
        await using (var hold = new NpgsqlCommand(
            "SELECT attempts FROM email_send_daily_counts WHERE day = DATE '2031-05-05' AND kind = 5 AND outcome = 6 FOR UPDATE",
            locker, transaction))
        {
            (await hold.ExecuteScalarAsync(Ct)).ShouldBe(1);
        }

        var clock = Stopwatch.StartNew();
        await sut.RecordAsync(EmailKind.LoginChallenge, EmailSendOutcome.Timeout);

        clock.Elapsed.ShouldBeLessThan(WithinTheBudget);
        var warning = _logger.Records.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.EventId.Id.ShouldBe(3009);
        await transaction.RollbackAsync(Ct);
    }

    /// <summary>
    /// A composition with a fixed clock. It is warmed before it is handed out: building EF's model for these context
    /// options takes longer than the tally's budget, and what is measured here is the tally, not a cold model.
    /// </summary>
    private async Task<ServiceProvider> CompositionAsync(DateTimeOffset now)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(MigrationsOptionsFactory.BuildAppOptions(fixture.ConnectionString)));
        services.AddSingleton<IDateTimeProvider>(new FixedClock(now));
        services.AddSingleton<ISwedishCalendar, SwedishCalendar>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("SELECT 1", Ct);
        return provider;
    }

    private async Task<int> AttemptsAsync(DateOnly day, EmailKind kind, EmailSendOutcome outcome)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(sum(attempts), 0)::int FROM email_send_daily_counts WHERE day = @day AND kind = @kind AND outcome = @outcome",
            connection);
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("kind", (short)kind);
        command.Parameters.AddWithValue("outcome", (short)outcome);
        return (int)(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>
    /// Opens as many pooled connections as the test will use at once, so the concurrent writes measure the upsert and
    /// not the opening of new connections to a container, which alone can outlast the budget.
    /// </summary>
    private async Task FillThePoolAsync(int connections)
    {
        var opened = await Task.WhenAll(Enumerable.Range(0, connections).Select(async _ =>
        {
            var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(Ct);
            return connection;
        }));
        foreach (var connection in opened)
            await connection.DisposeAsync();
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Records)
                Records.Add((logLevel, eventId, formatter(state, exception)));
        }
    }
}
