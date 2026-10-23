using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Jobs.EmailSendCountRetention;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Email.Counting;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Time;
using Jobbliggaren.Worker.Hosting;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Email;

/// <summary>
/// #1981 PR 3 (ADR 0181) — the retention of our own send counts against the migrated schema, through the Worker's own
/// registration: a strict cutoff that keeps the first date the seven-day period reads, nothing more taken on a second run,
/// and the recording start never touched. Counts are written by the production tally under fixed clocks, on 2029 dates
/// no other test uses.
/// </summary>
[Collection("Worker")]
public sealed class EmailSendCountRetentionJobIntegrationTests(WorkerTestFixture fixture) : IAsyncLifetime
{
    // 04:40 UTC on 16 November 2029 is 05:40 in Stockholm: the scheduled run keeps 9 November and after.
    private static readonly DateTimeOffset ScheduledRun = new(2029, 11, 16, 4, 40, 0, TimeSpan.Zero);
    private static readonly DateOnly FirstKept = new(2029, 11, 9);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM email_send_daily_counts WHERE day BETWEEN DATE '2029-01-01' AND DATE '2029-12-31'", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public void TheWorker_ResolvesThroughTheHostsRegistration()
    {
        using var scope = fixture.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<EmailSendCountRetentionWorker>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IEmailSendCountRetention>().ShouldBeOfType<SqlEmailSendCountRetention>();
    }

    [Fact]
    public async Task RunAsync_DeletesOnlyTheDatesBeforeTheFirstKept_AndASecondRunTakesNothingMore()
    {
        foreach (var day in new[] { FirstKept.AddDays(-2), FirstKept.AddDays(-1), FirstKept, FirstKept.AddDays(1) })
            await TallyAt(day).RecordAsync(EmailKind.MatchNotification, EmailSendOutcome.Accepted);
        var start = await StartAsync();

        await JobAt(ScheduledRun).RunAsync(Ct);

        (await DaysAsync()).ShouldBe([FirstKept, FirstKept.AddDays(1)]);
        await JobAt(ScheduledRun).RunAsync(Ct);
        (await DaysAsync()).ShouldBe([FirstKept, FirstKept.AddDays(1)], "a second run takes nothing more");
        (await StartAsync()).ShouldBe(start, "the retention never touches the recording start");
    }

    private EmailSendCountRetentionJob JobAt(DateTimeOffset now)
    {
        var scope = fixture.Services.CreateScope();
        return new EmailSendCountRetentionJob(
            scope.ServiceProvider.GetRequiredService<IEmailSendCountRetention>(),
            new FixedClock(now),
            new SwedishCalendar(),
            NullLogger<EmailSendCountRetentionJob>.Instance);
    }

    private SqlEmailSendTally TallyAt(DateOnly day)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(MigrationsOptionsFactory.BuildAppOptions(fixture.ConnectionString)));
        services.AddSingleton<IDateTimeProvider>(new FixedClock(new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)));
        services.AddSingleton<ISwedishCalendar, SwedishCalendar>();
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRaw("SELECT 1");
        return new SqlEmailSendTally(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SqlEmailSendTally>.Instance);
    }

    private async Task<List<DateOnly>> DaysAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT day FROM email_send_daily_counts WHERE day BETWEEN DATE '2029-01-01' AND DATE '2029-12-31' ORDER BY day",
            connection);
        var days = new List<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
            days.Add(reader.GetFieldValue<DateOnly>(0));
        return days;
    }

    private async Task<DateTimeOffset> StartAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT started_at FROM email_send_counting", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        return reader.GetFieldValue<DateTimeOffset>(0);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
