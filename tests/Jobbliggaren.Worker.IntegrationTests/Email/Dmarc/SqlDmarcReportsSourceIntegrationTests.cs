using System.Net;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using static Jobbliggaren.Worker.IntegrationTests.Email.Dmarc.DmarcTestReports;

namespace Jobbliggaren.Worker.IntegrationTests.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — the page's read against the migrated schema: which reports a window holds, the conflict rule
/// over every stored version, the largest sources in their fixed order with totals over all of them, and the ledger
/// counts by receipt. Every row is written by the production store, on 2032 dates no other test uses.
/// </summary>
[Collection("Worker")]
public sealed class SqlDmarcReportsSourceIntegrationTests(WorkerTestFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset WindowStart = new(2032, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = WindowStart.AddDays(7);
    private AsyncServiceScope _scope;
    private AppDbContext _db = null!;
    private DmarcIngestionDriver _driver = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await ClearAsync(fixture.ConnectionString);
        _scope = fixture.Services.CreateAsyncScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _driver = new DmarcIngestionDriver(Store(_db));
        await _driver.StartAsync(WindowStart.AddDays(-30));
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await ClearAsync(fixture.ConnectionString);
    }

    [Fact]
    public async Task Read_WithNothingStored_ReturnsZerosAndNoCoverage()
    {
        await ClearAsync(fixture.ConnectionString);

        var read = await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct);

        read.State.ShouldBeNull();
        read.Sources.ShouldBeEmpty();
        read.Totals.ShouldBe(new DmarcReportsTotals(0, 0, 0, 0, 0, 0, null, null));
        read.MessagesReceivedInWindow.ShouldBeEmpty();
        read.RetryPending.ShouldBe(0);
    }

    /// <summary>A report is in the window when its period overlaps it, both ends half-open.</summary>
    [Fact]
    public async Task Read_HoldsTheReportsWhosePeriodOverlapsTheWindow_HalfOpenAtBothEnds()
    {
        await StoreAsync("ends-at-start", WindowStart.AddDays(-1), WindowStart, "192.0.2.1", 1);
        await StoreAsync("ends-after-start", WindowStart.AddDays(-1), WindowStart.AddSeconds(1), "192.0.2.2", 2);
        await StoreAsync("begins-before-end", WindowEnd.AddSeconds(-1), WindowEnd.AddDays(1), "192.0.2.3", 4);
        await StoreAsync("begins-at-end", WindowEnd, WindowEnd.AddDays(1), "192.0.2.4", 8);

        var read = await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct);

        read.Sources.Select(s => s.Source.ToString()).Order().ShouldBe(["192.0.2.2", "192.0.2.3"]);
        read.Totals.ShouldBe(new DmarcReportsTotals(
            Reports: 2, Reporters: 1, SourceCount: 2, Messages: 6, FailingMessages: 0, ConflictingReports: 0,
            CoveredFrom: WindowStart.AddDays(-1), CoveredTo: WindowEnd.AddDays(1)));
    }

    /// <summary>
    /// security-auditor A1/A2: a reporter and report id stored with more than one digest is judged over every stored
    /// version, inside the window or not. None of its versions counts towards any figure, and it is counted once when
    /// any version overlaps the window.
    /// </summary>
    [Fact]
    public async Task Read_ExcludesAConflictingReportFromEveryFigure_AndCountsItOnce()
    {
        var inside = WindowStart.AddDays(2);
        var before = WindowStart.AddDays(-20);
        (await StoreAsync("c-1", inside, inside.AddDays(1), "192.0.2.10", 100, content: "a")).ShouldBe(DmarcMessageOutcome.Stored);
        (await StoreAsync("c-1", before, before.AddDays(1), "192.0.2.11", 100, content: "b"))
            .ShouldBe(DmarcMessageOutcome.StoredConflicting, "the other version lies outside the window");
        (await StoreAsync("c-2", inside, inside.AddDays(1), "192.0.2.12", 100, content: "a")).ShouldBe(DmarcMessageOutcome.Stored);
        (await StoreAsync("c-2", inside, inside.AddDays(1), "192.0.2.13", 100, content: "b"))
            .ShouldBe(DmarcMessageOutcome.StoredConflicting);
        await StoreAsync("c-3", before, before.AddDays(1), "192.0.2.14", 100, content: "a");
        await StoreAsync("c-3", before, before.AddDays(1), "192.0.2.15", 100, content: "b");
        await StoreAsync("ok-1", inside, inside.AddDays(1), "198.51.100.1", 5, content: "a");
        (await StoreAsync("c-1", inside, inside.AddDays(1), "198.51.100.2", 7, reporter: "other.example", content: "c"))
            .ShouldBe(DmarcMessageOutcome.Stored, "the group is the reporter and its id together");

        var read = await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct);

        read.Sources.Select(s => s.Source.ToString()).Order().ShouldBe(["198.51.100.1", "198.51.100.2"]);
        read.Totals.ShouldBe(new DmarcReportsTotals(
            Reports: 2, Reporters: 2, SourceCount: 2, Messages: 12, FailingMessages: 0, ConflictingReports: 2,
            CoveredFrom: inside, CoveredTo: inside.AddDays(1)));
    }

    [Fact]
    public async Task Read_OrdersTheSourcesFailingFirst_CapsTheRows_AndTotalsEverySource()
    {
        var at = WindowStart.AddDays(1);
        await _driver.StoreAsync(Accepted("order-1", at.AddDays(1), [
            Record("203.0.113.5", 10, "fail", "fail"),
            Record("2001:db8::5", 10, "fail", "fail"),
            Record("192.0.2.9", 5, "temperror", "fail"),
            Record("198.51.100.7", 50, "pass", "fail"),
            Record("192.0.2.1", 50, "pass", "pass", headerFrom: "mail.jobbliggaren.se"),
        ], beginsAt: at, endsAt: at.AddDays(1)));
        await _driver.StoreAsync(Accepted("order-2", at.AddDays(1), [
            Record("198.51.100.7", 20, "pass", "fail"),
            Record("192.0.2.1", 50, "fail", "pass", headerFrom: "bounce.jobbliggaren.se"),
            Record("203.0.113.6", 4, "fail", "pass"),
        ], reporter: "second.example", beginsAt: at, endsAt: at.AddDays(1)));

        var read = await Source().ReadAsync(WindowStart, WindowEnd, 4, Ct);

        read.Sources.ShouldBe([
            new DmarcSourceRow(Address("203.0.113.5"), "jobbliggaren.se", 10, 0, 0, 10),
            new DmarcSourceRow(Address("2001:db8::5"), "jobbliggaren.se", 10, 0, 0, 10),
            new DmarcSourceRow(Address("192.0.2.9"), "jobbliggaren.se", 5, 0, 0, 5),
            new DmarcSourceRow(Address("198.51.100.7"), "jobbliggaren.se", 70, 70, 0, 0),
        ], "failing first, then failing, messages, the IPv4 family before IPv6, address and domain");
        read.Totals.ShouldBe(new DmarcReportsTotals(
            Reports: 2, Reporters: 2, SourceCount: 7, Messages: 199, FailingMessages: 25, ConflictingReports: 0,
            CoveredFrom: at, CoveredTo: at.AddDays(1)));

        (await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct)).Sources.Skip(4)
            .Select(s => $"{s.Source}|{s.HeaderFrom}|{s.Messages}|{s.DkimAligned}|{s.SpfAligned}")
            .ShouldBe([
                "192.0.2.1|bounce.jobbliggaren.se|50|0|50",
                "192.0.2.1|mail.jobbliggaren.se|50|50|50",
                "203.0.113.6|jobbliggaren.se|4|0|4",
            ], "a tie on messages goes by address, then by domain in ordinal order");
    }

    [Fact]
    public async Task Read_CountsTheLedgerByReceiptInTheWindow_AndEveryMessageWaitingForARetry()
    {
        await NewStore().RecordAttemptAsync(WindowEnd, DmarcRunOutcome.Timeout, Ct);
        await Claim(WindowStart.AddMicroseconds(-1), DmarcMessageOutcome.SkippedForensic);
        await Claim(WindowStart, DmarcMessageOutcome.SkippedForensic);
        await Claim(WindowStart.AddDays(3), DmarcMessageOutcome.SkippedForensic);
        await Claim(WindowStart.AddDays(3), DmarcMessageOutcome.RetryPending);
        await Claim(WindowEnd.AddMicroseconds(-1), DmarcMessageOutcome.RejectedContent);
        await Claim(WindowEnd, DmarcMessageOutcome.RetryPending);
        await _driver.StoreAsync(Accepted("ledger-1", WindowStart.AddDays(4), [Record("192.0.2.70", 1)]));

        var read = await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct);

        read.MessagesReceivedInWindow.ShouldBe([
            new DmarcOutcomeCount(DmarcMessageOutcome.RetryPending, 1),
            new DmarcOutcomeCount(DmarcMessageOutcome.Stored, 1),
            new DmarcOutcomeCount(DmarcMessageOutcome.SkippedForensic, 2),
            new DmarcOutcomeCount(DmarcMessageOutcome.RejectedContent, 1),
        ]);
        read.RetryPending.ShouldBe(2, "a retry waits whenever it was received");
        read.State.ShouldBe(new DmarcRunState(WindowEnd, DmarcRunOutcome.Timeout, null, new DmarcCursor(Epoch, 7)));
    }

    /// <summary>
    /// The address is stored as the reporter's source names it and read back as the same address: IPv4, IPv6, and an
    /// IPv4-mapped IPv6 address as the IPv4 address it names, so one server is one source.
    /// </summary>
    [Fact]
    public async Task Read_ReturnsEachStoredAddressAsTheSourceItNames()
    {
        var at = WindowStart.AddDays(1);
        await _driver.StoreAsync(Accepted("inet-1", at.AddDays(1), [
            Record("203.0.113.80", 1, "fail", "fail"),
            Record("2001:db8:0:0::80", 2, "fail", "fail"),
            Record("::ffff:203.0.113.80", 4, "fail", "fail"),
        ], beginsAt: at, endsAt: at.AddDays(1)));

        var read = await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct);

        read.Sources.Select(s => (s.Source.Value, s.Messages)).ShouldBe([
            (IPAddress.Parse("203.0.113.80"), 5L),
            (IPAddress.Parse("2001:db8::80"), 2L),
        ]);
        read.Sources[0].Source.Value.AddressFamily.ShouldBe(System.Net.Sockets.AddressFamily.InterNetwork);
    }

    /// <summary>
    /// Unreachable through this release's writer, which stores only defined members: only a later release's writer
    /// followed by an image rollback can leave such a value, so each is written by hand, and the test asserts only that
    /// the read fails instead of counting around what this release cannot vouch for.
    /// </summary>
    [Fact]
    public async Task Read_AStoredValueThisReleaseDoesNotKnow_FailsTheRead()
    {
        var at = WindowStart.AddDays(1);
        await _driver.StoreAsync(Accepted("unknown-1", at.AddDays(1), [Record("192.0.2.90", 1)], beginsAt: at, endsAt: at.AddDays(1)));

        await ExecuteAsync("UPDATE dmarc_report_rows SET dkim_aligned = 99");
        await Should.ThrowAsync<InvalidOperationException>(async () => await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct));
        await ExecuteAsync("UPDATE dmarc_report_rows SET dkim_aligned = 1, spf_aligned = 99");
        await Should.ThrowAsync<InvalidOperationException>(async () => await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct));
        await ExecuteAsync("UPDATE dmarc_report_rows SET spf_aligned = 1; UPDATE dmarc_mailbox_messages SET outcome = 99");
        await Should.ThrowAsync<InvalidOperationException>(async () => await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct));
        await ExecuteAsync("UPDATE dmarc_mailbox_messages SET outcome = 10; UPDATE dmarc_mailbox_state SET last_outcome = 99");
        await Should.ThrowAsync<InvalidOperationException>(async () => await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct));
        await ExecuteAsync("UPDATE dmarc_mailbox_state SET last_outcome = 9");

        (await Source().ReadAsync(WindowStart, WindowEnd, 10, Ct)).Totals.Reports.ShouldBe(1, "the restored values read again");
    }

    private async Task Claim(DateTimeOffset receivedAt, DmarcMessageOutcome outcome)
    {
        var position = await _driver.ClaimAsync(receivedAt);
        await NewStore().SetOutcomeAsync(_driver.Epoch, position, outcome, Ct);
    }

    private Task<DmarcMessageOutcome> StoreAsync(
        string reportId, DateTimeOffset beginsAt, DateTimeOffset endsAt, string source, long count,
        string reporter = "reporter.example", string? content = null) =>
        _driver.StoreAsync(Accepted(
            reportId, endsAt, [Record(source, count)], reporter, content ?? reportId, beginsAt, endsAt));

    private static ReportedSourceAddress Address(string text)
    {
        ReportedSourceAddress.TryParse(text, out var address).ShouldBeTrue();
        return address!;
    }

    private SqlDmarcReportStore NewStore() => Store(_db);

    private SqlDmarcReportsSource Source() => new(_db);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
