using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Email.Counting;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Time;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Email;

/// <summary>
/// #1981 PR 3 (ADR 0180) — the SQL read of our own counts against the migrated schema: the window is inclusive at both
/// ends and holds nothing outside it, and a read fails rather than count around what this release cannot vouch for.
/// Counts are written by the production tally under a fixed clock, on 2030 dates no other test uses.
/// </summary>
[Collection("Worker")]
public sealed class SqlEmailSendCountsSourceIntegrationTests(WorkerTestFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2030, 6, 10);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() =>
        await ExecuteAsync("DELETE FROM email_send_daily_counts WHERE day BETWEEN DATE '2030-01-01' AND DATE '2030-12-31'");

    [Fact]
    public async Task ReadAsync_ReturnsEveryCountFromTheFirstDateThroughTheLast_AndNothingOutside()
    {
        foreach (var offset in new[] { -1, 0, 1, 2 })
            await TallyAt(Day.AddDays(offset)).RecordAsync(EmailKind.FollowedCompanyNotification, EmailSendOutcome.Accepted);

        var read = (await Source().ReadAsync(Day, Day.AddDays(1), Ct)).ShouldBeOfType<EmailSendCountsRead.Observed>();

        read.Rows.Select(row => row.Day).Order().ShouldBe([Day, Day.AddDays(1)]);
        read.Rows.ShouldAllBe(row => row.Kind == EmailKind.FollowedCompanyNotification
            && row.Outcome == EmailSendOutcome.Accepted && row.Attempts == 1);
        read.RecordingStartedAt.ShouldBe(await StartAsync());
    }

    /// <summary>
    /// Unreachable through this release's writer, which counts only defined members: only a later release's writer
    /// followed by an image rollback can leave such a row, so it is written by hand, and the test asserts only that the
    /// read fails safely instead of understating what it shows.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AStoredValueThisReleaseDoesNotKnow_FailsTheRead()
    {
        await ExecuteAsync("INSERT INTO email_send_daily_counts (day, kind, outcome, attempts) VALUES (DATE '2030-06-10', 99, 1, 1)");

        await Should.ThrowAsync<InvalidOperationException>(async () => await Source().ReadAsync(Day, Day, Ct));
    }

    /// <summary>
    /// Unreachable through any code: the migration writes the start and nothing deletes it, so only a hand edit can leave
    /// the table empty. The row is put back as it was.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WithoutARecordingStart_FailsTheRead()
    {
        var start = await StartAsync();
        await ExecuteAsync("DELETE FROM email_send_counting");
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(async () => await Source().ReadAsync(Day, Day, Ct));
        }
        finally
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(Ct);
            await using var restore = new NpgsqlCommand(
                "INSERT INTO email_send_counting (id, started_at) VALUES (1, @start)", connection);
            restore.Parameters.AddWithValue("start", start);
            await restore.ExecuteNonQueryAsync(Ct);
        }
    }

    private SqlEmailSendCountsSource Source() =>
        new(fixture.Services.GetRequiredService<IServiceScopeFactory>());

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

    private async Task<DateTimeOffset> StartAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT started_at FROM email_send_counting", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        return reader.GetFieldValue<DateTimeOffset>(0);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
