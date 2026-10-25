using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using static Jobbliggaren.Worker.IntegrationTests.Email.Dmarc.DmarcTestReports;

namespace Jobbliggaren.Worker.IntegrationTests.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — the ingestion store against the migrated schema: the state row, the cursor and its epochs,
/// the claim ledger with its crash and retry bounds, and the report write with its daily caps, its content key and its
/// conflict outcome. Every row is written by the production store, in the order the ingestion job calls it, on 2031
/// dates no other test uses.
/// </summary>
[Collection("Worker")]
public sealed class SqlDmarcReportStoreIntegrationTests(WorkerTestFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Day = new(2031, 5, 12, 0, 0, 0, TimeSpan.Zero);
    private AsyncServiceScope _scope;
    private SqlDmarcReportStore _store = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await ClearAsync(fixture.ConnectionString);
        _scope = fixture.Services.CreateAsyncScope();
        _store = Store(_scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await ClearAsync(fixture.ConnectionString);
    }

    [Fact]
    public async Task EnsureState_WritesAnIncompleteFirstRunOnce_AndNeverOverwritesALaterAttempt()
    {
        (await _store.ReadStateAsync(Ct)).ShouldBeNull("no row is the true state 'never attempted'");

        await _store.EnsureStateAsync(Day, Ct);
        (await _store.ReadStateAsync(Ct)).ShouldBe(new DmarcRunState(Day, DmarcRunOutcome.Incomplete, null, null));

        await _store.RecordAttemptAsync(Day.AddHours(1), DmarcRunOutcome.Succeeded, Ct);
        await _store.EnsureStateAsync(Day.AddHours(2), Ct);

        (await _store.ReadStateAsync(Ct)).ShouldBe(
            new DmarcRunState(Day.AddHours(1), DmarcRunOutcome.Succeeded, Day.AddHours(1), null));
    }

    [Fact]
    public async Task RecordAttempt_MovesTheLastSuccessOnlyOnASuccess_AndWritesTheRowWhenNoneExists()
    {
        await _store.RecordAttemptAsync(Day, DmarcRunOutcome.NotConfigured, Ct);
        (await _store.ReadStateAsync(Ct)).ShouldBe(new DmarcRunState(Day, DmarcRunOutcome.NotConfigured, null, null));

        await _store.RecordAttemptAsync(Day.AddHours(1), DmarcRunOutcome.Succeeded, Ct);
        await _store.RecordAttemptAsync(Day.AddHours(2), DmarcRunOutcome.AuthRefused, Ct);

        (await _store.ReadStateAsync(Ct)).ShouldBe(
            new DmarcRunState(Day.AddHours(2), DmarcRunOutcome.AuthRefused, Day.AddHours(1), null));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await _store.RecordAttemptAsync(Day, DmarcRunOutcome.Incomplete, Ct));
    }

    [Fact]
    public async Task EnterEpoch_StartsTheCursorAtZero_ForgetsTheOldEpochsLedger_AndKeepsTheStoredReports()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () => await _store.EnterEpochAsync(Epoch, Ct));
        var first = new DmarcIngestionDriver(_store, Epoch);
        await first.StartAsync(Day);
        (await _store.ReadStateAsync(Ct))!.Cursor.ShouldBe(new DmarcCursor(Epoch, 0));
        (await first.StoreAsync(Accepted("epoch-1", Day.AddHours(1), [Record("192.0.2.1", 1)])))
            .ShouldBe(DmarcMessageOutcome.Stored);
        await first.ClaimAsync(Day.AddHours(2));

        (await _store.EnterEpochAsync(Epoch, Ct)).ShouldBeFalse("the cursor is already in this epoch");
        (await _store.ReadStateAsync(Ct))!.Cursor.ShouldBe(new DmarcCursor(Epoch, 2));

        (await _store.EnterEpochAsync(Epoch + 1, Ct)).ShouldBeTrue();

        (await _store.ReadStateAsync(Ct))!.Cursor.ShouldBe(new DmarcCursor(Epoch + 1, 0));
        (await LedgerAsync()).ShouldBeEmpty("the old epoch's positions mean nothing in the new one");
        (await Count("dmarc_reports")).ShouldBe(1, "a stored report stays; its content key keeps a rescan from storing it twice");
    }

    [Fact]
    public async Task Claim_WritesAnInProgressRowAndAdvancesTheCursor_AndNeverClaimsAtOrBelowIt()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);

        await _store.ClaimAsync(Epoch, 5, Day.AddMinutes(3), Ct);

        (await LedgerAsync()).ShouldBe([$"{Epoch}|5|{Day.AddMinutes(3).ToUnixTimeSeconds()}|1|1|0"]);
        (await _store.ReadStateAsync(Ct))!.Cursor.ShouldBe(new DmarcCursor(Epoch, 5));
        await Should.ThrowAsync<InvalidOperationException>(async () => await _store.ClaimAsync(Epoch, 5, Day, Ct));
        await Should.ThrowAsync<InvalidOperationException>(async () => await _store.ClaimAsync(Epoch, 4, Day, Ct));
        await Should.ThrowAsync<InvalidOperationException>(async () => await _store.ClaimAsync(Epoch + 1, 9, Day, Ct));
        (await LedgerAsync()).Count.ShouldBe(1, "a refused claim writes nothing");
        (await _store.ReadStateAsync(Ct))!.Cursor.ShouldBe(new DmarcCursor(Epoch, 5));
    }

    [Fact]
    public async Task AdvanceCursor_PassesAMessageReceivedBeforeTheCutoff_WithoutALedgerRow()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);

        await _store.AdvanceCursorAsync(Epoch, 7, Ct);

        (await _store.ReadStateAsync(Ct))!.Cursor.ShouldBe(new DmarcCursor(Epoch, 7));
        (await LedgerAsync()).ShouldBeEmpty();
        await Should.ThrowAsync<InvalidOperationException>(async () => await _store.AdvanceCursorAsync(Epoch, 7, Ct));
    }

    [Fact]
    public async Task SweepCrashes_RequeuesAFirstCrash_AndAbandonsASecond()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        var crashed = await driver.ClaimAsync(Day);
        var finished = await driver.ClaimAsync(Day);
        await _store.SetOutcomeAsync(Epoch, finished, DmarcMessageOutcome.SkippedForensic, Ct);

        (await _store.SweepCrashesAsync(Ct)).ShouldBe(new DmarcCrashSweep(Requeued: 1, Abandoned: 0));
        (await RowAsync(crashed)).ShouldBe("2|2|0", "RetryPending, its second attempt to come");

        (await _store.ClaimRetryAsync(Epoch, crashed, Ct)).ShouldBeTrue();
        (await _store.SweepCrashesAsync(Ct)).ShouldBe(new DmarcCrashSweep(Requeued: 0, Abandoned: 1));

        (await RowAsync(crashed)).ShouldBe("3|2|1", "Abandoned after two runs ended without finishing it");
        (await RowAsync(finished)).ShouldBe("23|1|0", "a finished message is never touched by the sweep");
        (await _store.SweepCrashesAsync(Ct)).ShouldBe(new DmarcCrashSweep(0, 0));
    }

    [Fact]
    public async Task ListRetries_ReturnsTheEpochsWaitingMessagesInPositionOrder_UpToTheLimit()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        var positions = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            var position = await driver.ClaimAsync(Day.AddMinutes(i));
            await _store.SetOutcomeAsync(
                Epoch, position, i == 2 ? DmarcMessageOutcome.RejectedMalformed : DmarcMessageOutcome.RetryPending, Ct);
            positions.Add(position);
        }

        (await _store.ListRetriesAsync(Epoch, DateTimeOffset.MinValue, 2, Ct)).ShouldBe([
            new DmarcPendingMessage(positions[0], Day, 0),
            new DmarcPendingMessage(positions[1], Day.AddMinutes(1), 0),
        ]);
        (await _store.ListRetriesAsync(Epoch, DateTimeOffset.MinValue, 10, Ct)).Select(m => m.Position).ShouldBe([positions[0], positions[1], positions[3]]);
        (await _store.ListRetriesAsync(Epoch, DateTimeOffset.MinValue, 0, Ct)).ShouldBeEmpty();
        (await _store.ListRetriesAsync(Epoch + 1, DateTimeOffset.MinValue, 10, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ListRetries_LeavesOutAWaitingMessageReceivedBeforeTheCutoff()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        var positions = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var position = await driver.ClaimAsync(Day.AddMinutes(i));
            await _store.SetOutcomeAsync(Epoch, position, DmarcMessageOutcome.RetryPending, Ct);
            positions.Add(position);
        }

        (await _store.ListRetriesAsync(Epoch, Day.AddMinutes(1), 10, Ct)).Select(m => m.Position)
            .ShouldBe([positions[1], positions[2]], "received at the cutoff is kept");
        (await _store.ListRetriesAsync(Epoch, Day.AddMinutes(1).AddMicroseconds(1), 10, Ct)).Select(m => m.Position)
            .ShouldBe([positions[2]]);
    }

    /// <summary>The 168 retries are made the way a run makes them: claim the retry, then hand it back.</summary>
    [Fact]
    public async Task ClaimRetry_CountsEachRetry_AndAbandonsTheMessageOnThePickupAfterTheLimit()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        var position = await driver.ClaimAsync(Day);
        await _store.SetOutcomeAsync(Epoch, position, DmarcMessageOutcome.RetryPending, Ct);

        for (var retry = 1; retry <= DmarcIngestionLimits.RetryLimit; retry++)
        {
            (await _store.ClaimRetryAsync(Epoch, position, Ct)).ShouldBeTrue();
            await _store.SetOutcomeAsync(Epoch, position, DmarcMessageOutcome.RetryPending, Ct);
        }

        (await RowAsync(position)).ShouldBe($"2|1|{DmarcIngestionLimits.RetryLimit}");
        (await _store.ClaimRetryAsync(Epoch, position, Ct)).ShouldBeFalse();
        (await RowAsync(position)).ShouldBe($"3|1|{DmarcIngestionLimits.RetryLimit}", "Abandoned without another fetch");
        await Should.ThrowAsync<InvalidOperationException>(async () => await _store.ClaimRetryAsync(Epoch, position, Ct));
    }

    [Fact]
    public async Task SetOutcome_EndsOnlyAClaimedTurn_AndOnlyWithAnOutcomeThatStoresNothing()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        var position = await driver.ClaimAsync(Day);

        foreach (var storing in new[]
                 {
                     DmarcMessageOutcome.InProgress, DmarcMessageOutcome.Abandoned, DmarcMessageOutcome.Stored,
                     DmarcMessageOutcome.Duplicate, DmarcMessageOutcome.StoredConflicting,
                     DmarcMessageOutcome.VolumeCapped, DmarcMessageOutcome.RejectedByStore, (DmarcMessageOutcome)99,
                 })
        {
            await Should.ThrowAsync<ArgumentOutOfRangeException>(
                async () => await _store.SetOutcomeAsync(Epoch, position, storing, Ct));
        }

        await _store.SetOutcomeAsync(Epoch, position, DmarcMessageOutcome.RejectedPolicyDomain, Ct);
        (await RowAsync(position)).ShouldBe("31|1|0");
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await _store.SetOutcomeAsync(Epoch, position, DmarcMessageOutcome.RetryPending, Ct));
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await _store.SetOutcomeAsync(Epoch, position + 1, DmarcMessageOutcome.RetryPending, Ct));
    }

    [Fact]
    public async Task Store_WritesTheReportAndItsRecordsInOrder_ADuplicateOnce_AndAnotherDigestAsAConflict()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        var received = Day.AddHours(9);
        var report = Accepted(
            "store-1",
            received,
            [Record("192.0.2.20", 7, "fail", "pass"), Record("2001:db8::20", 3, "pass", "temperror"), Record("::ffff:198.51.100.20", 1)],
            content: "version a");

        (await driver.StoreAsync(report)).ShouldBe(DmarcMessageOutcome.Stored);
        (await driver.StoreAsync(Accepted("store-1", received.AddHours(1), [Record("192.0.2.20", 7)], content: "version a")))
            .ShouldBe(DmarcMessageOutcome.Duplicate, "the same reporter, report id and content is the same report");
        (await driver.StoreAsync(Accepted("store-1", received.AddHours(2), [Record("192.0.2.21", 2)], content: "version b")))
            .ShouldBe(DmarcMessageOutcome.StoredConflicting);
        (await driver.StoreAsync(Accepted("store-1", received.AddHours(3), [Record("192.0.2.22", 2)], reporter: "other.example")))
            .ShouldBe(DmarcMessageOutcome.Stored, "the same report id from another reporter is another report");

        (await Strings(
            "SELECT rr.ordinal || '|' || host(rr.source_ip) || '|' || family(rr.source_ip) || '|' || rr.message_count "
            + "|| '|' || rr.header_from || '|' || rr.dkim_aligned || '|' || rr.spf_aligned "
            + "FROM dmarc_report_rows rr JOIN dmarc_reports r ON r.id = rr.report_id "
            + $"WHERE r.received_at = '{received:O}' ORDER BY rr.ordinal"))
            .ShouldBe([
                "0|192.0.2.20|4|7|jobbliggaren.se|2|1",
                "1|2001:db8::20|6|3|jobbliggaren.se|1|3",
                "2|198.51.100.20|4|1|jobbliggaren.se|1|1",
            ], "a mapped source is stored as the IPv4 address it names");
        (await Strings(
            "SELECT reporter || '|' || reporter_report_id || '|' || octet_length(content_sha256) || '|' "
            + "|| extract(epoch FROM begins_at)::bigint || '|' || extract(epoch FROM ends_at)::bigint "
            + $"FROM dmarc_reports WHERE received_at = '{received:O}'"))
            .ShouldBe([$"reporter.example|store-1|32|{report.BeginsAt.ToUnixTimeSeconds()}|{report.EndsAt.ToUnixTimeSeconds()}"]);
        (await Count("dmarc_reports")).ShouldBe(3);
        (await Strings("SELECT outcome::text FROM dmarc_mailbox_messages ORDER BY uid"))
            .ShouldBe(["10", "11", "12", "10"], "the outcome is recorded in the store's own transaction");
    }

    [Fact]
    public async Task Store_CapsTheReportsOfOneUtcDay_AndTheNextDayStartsAfresh()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        for (var i = 0; i < DmarcIngestionLimits.MaxStoredReportsPerUtcDay; i++)
        {
            var received = i == 0 ? Day : Day.AddDays(1).AddMicroseconds(-1);
            (await driver.StoreAsync(Accepted($"cap-{i}", received, [Record("192.0.2.30", 1)])))
                .ShouldBe(DmarcMessageOutcome.Stored);
        }

        (await driver.StoreAsync(Accepted("cap-over", Day.AddHours(12), [Record("192.0.2.30", 1)])))
            .ShouldBe(DmarcMessageOutcome.VolumeCapped);
        (await driver.StoreAsync(Accepted("cap-0", Day.AddHours(13), [Record("192.0.2.30", 1)])))
            .ShouldBe(DmarcMessageOutcome.Duplicate, "the same report is already stored, on a capped day too");
        (await driver.StoreAsync(Accepted("cap-0", Day.AddHours(14), [Record("192.0.2.30", 1)], content: "other content")))
            .ShouldBe(DmarcMessageOutcome.VolumeCapped, "the same key with other content would be stored");
        (await driver.StoreAsync(Accepted("cap-next", Day.AddDays(1), [Record("192.0.2.30", 1)])))
            .ShouldBe(DmarcMessageOutcome.Stored, "the next UTC day begins at midnight UTC, half-open");

        (await Count("dmarc_reports")).ShouldBe(DmarcIngestionLimits.MaxStoredReportsPerUtcDay + 1);
    }

    // The largest report Accept lets through: every text field at its length bound and a record at its count bound.
    [Fact]
    public async Task Store_TheLargestAcceptedReport_IsStored()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        const string policy = DmarcIngestionLimits.PolicyDomain;
        var label = new string('a', 63) + ".";
        var filler = new string('b', DmarcIngestionLimits.MaxHeaderFromLength - (3 * label.Length) - policy.Length - 1);
        var headerFrom = label + label + label + filler + "." + policy;
        headerFrom.Length.ShouldBe(DmarcIngestionLimits.MaxHeaderFromLength);
        var report = Accepted(
            new string('i', DmarcIngestionLimits.MaxFieldLength),
            Day.AddHours(1),
            [Record("192.0.2.70", DmarcIngestionLimits.MaxRecordMessageCount, headerFrom: headerFrom)],
            reporter: new string('r', DmarcIngestionLimits.MaxFieldLength));

        (await driver.StoreAsync(report)).ShouldBe(DmarcMessageOutcome.Stored);
        (await Strings("SELECT length(reporter) || '|' || length(reporter_report_id) FROM dmarc_reports"))
            .ShouldBe([$"{DmarcIngestionLimits.MaxFieldLength}|{DmarcIngestionLimits.MaxFieldLength}"]);
        (await Strings("SELECT length(header_from) || '|' || message_count FROM dmarc_report_rows"))
            .ShouldBe([$"{DmarcIngestionLimits.MaxHeaderFromLength}|{DmarcIngestionLimits.MaxRecordMessageCount}"]);
    }

    [Fact]
    public async Task Store_CapsTheRecordsOfOneUtcDay()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        var full = Enumerable.Range(0, DmarcIngestionLimits.MaxStoredRecordsPerUtcDay - 1)
            .Select(i => Record($"203.0.113.{i % 250}", 1))
            .ToList();

        (await driver.StoreAsync(Accepted("records-1", Day.AddHours(1), full))).ShouldBe(DmarcMessageOutcome.Stored);
        (await driver.StoreAsync(Accepted("records-2", Day.AddHours(2), [Record("192.0.2.40", 1), Record("192.0.2.41", 1)])))
            .ShouldBe(DmarcMessageOutcome.VolumeCapped, "two more records would pass the day's cap");
        (await driver.StoreAsync(Accepted("records-3", Day.AddHours(3), [Record("192.0.2.42", 1)])))
            .ShouldBe(DmarcMessageOutcome.Stored, "one more record fills the day exactly");

        (await Count("dmarc_report_rows")).ShouldBe(DmarcIngestionLimits.MaxStoredRecordsPerUtcDay);
    }

    /// <summary>
    /// Declared unreachable: <see cref="DmarcAggregateReport.Accept"/> checks in advance every bound the DDL enforces,
    /// so this release's schema never refuses a report it accepted. A schema stricter than the release can: a later
    /// release's migration adding a constraint, followed by an image rollback. That later constraint is what the test
    /// adds, and it asserts only that the store degrades safely: the message is recorded as refused, nothing of the
    /// report is kept, and the log line carries a count and a code and no field of the report.
    /// </summary>
    [Fact]
    public async Task Store_ADatabaseThatRefusesAnAcceptedReport_RecordsTheRefusalAndKeepsNothing()
    {
        var logger = new ListLogger<SqlDmarcReportStore>();
        var store = Store(_scope.ServiceProvider.GetRequiredService<AppDbContext>(), logger);
        var driver = new DmarcIngestionDriver(store);
        await driver.StartAsync(Day);
        await ExecuteAsync(
            "ALTER TABLE dmarc_reports ADD CONSTRAINT ck_later_release_refusal CHECK (reporter <> 'refused.example')");
        try
        {
            (await driver.StoreAsync(Accepted("refused-1", Day, [Record("192.0.2.50", 1), Record("192.0.2.51", 1)], reporter: "refused.example")))
                .ShouldBe(DmarcMessageOutcome.RejectedByStore);
        }
        finally
        {
            await ExecuteAsync("ALTER TABLE dmarc_reports DROP CONSTRAINT ck_later_release_refusal");
        }

        (await Strings("SELECT outcome::text FROM dmarc_mailbox_messages")).ShouldBe(["33"]);
        (await Count("dmarc_reports")).ShouldBe(0);
        (await Count("dmarc_report_rows")).ShouldBe(0);
        var line = logger.Records.ShouldHaveSingleItem();
        line.EventId.Id.ShouldBe(3130);
        line.Level.ShouldBe(LogLevel.Warning);
        line.Message.ShouldContain("2 records");
        line.Message.ShouldContain("SQLSTATE 23514");
        line.Message.ShouldNotContain("refused.example");
        line.Message.ShouldNotContain("refused-1");
        line.Message.ShouldNotContain("192.0.2");
    }

    [Fact]
    public async Task Store_RefusesAMessageThatIsNotClaimed_AndKeepsNothing()
    {
        var driver = new DmarcIngestionDriver(_store);
        await driver.StartAsync(Day);
        var position = await driver.ClaimAsync(Day);
        await _store.SetOutcomeAsync(Epoch, position, DmarcMessageOutcome.SkippedTooLarge, Ct);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _store.StoreAsync(Epoch, position, Accepted("unclaimed-1", Day, [Record("192.0.2.60", 1)]), Ct));

        (await Count("dmarc_reports")).ShouldBe(0, "the report and the outcome are one transaction");
    }

    [Fact]
    public async Task TheRegistrations_ResolveTheStoreAndTheRetentionForTheWorker_AndTheSourceForTheApi()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new AppDbContext(MigrationsOptionsFactory.BuildAppOptions(fixture.ConnectionString)));
        services.AddDmarcReportIngestionStores();
        services.AddDmarcReportsSource();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IDmarcReportStore>().ShouldBeOfType<SqlDmarcReportStore>();
        scope.ServiceProvider.GetRequiredService<IDmarcReportRetention>().ShouldBeOfType<SqlDmarcReportRetention>();
        scope.ServiceProvider.GetRequiredService<IDmarcReportsSource>().ShouldBeOfType<SqlDmarcReportsSource>();
    }

    private async Task<string> RowAsync(long position) => (await Strings(
        $"SELECT outcome || '|' || attempts || '|' || retries FROM dmarc_mailbox_messages WHERE uid = {position}"))
        .ShouldHaveSingleItem();

    private Task<List<string>> LedgerAsync() => Strings(
        "SELECT uid_validity || '|' || uid || '|' || extract(epoch FROM received_at)::bigint || '|' || outcome || '|' "
        + "|| attempts || '|' || retries FROM dmarc_mailbox_messages ORDER BY uid");

    private async Task<long> Count(string table) =>
        long.Parse((await Strings($"SELECT count(*)::text FROM {table}")).ShouldHaveSingleItem(),
            System.Globalization.CultureInfo.InvariantCulture);

    private Task<List<string>> Strings(string sql) => ReadStringsAsync(fixture.ConnectionString, sql);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Records.Add((logLevel, eventId, formatter(state, exception)));
    }
}
