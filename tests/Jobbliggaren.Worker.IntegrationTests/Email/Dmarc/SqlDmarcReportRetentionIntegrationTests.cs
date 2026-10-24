using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Worker.IntegrationTests.Email.Dmarc.DmarcTestReports;

namespace Jobbliggaren.Worker.IntegrationTests.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the retention's delete against the migrated schema: a strict cutoff on receipt for reports
/// and ledger rows alike, the records going with their report through the database's cascade, nothing more on a second
/// run, and the state row never touched. Rows are written by the production store, on 2033 dates no other test uses.
/// </summary>
[Collection("Worker")]
public sealed class SqlDmarcReportRetentionIntegrationTests(WorkerTestFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2033, 9, 15, 4, 20, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(ClearAsync(fixture.ConnectionString));

    public ValueTask DisposeAsync() => new(ClearAsync(fixture.ConnectionString));

    [Fact]
    public async Task DeleteReceivedBefore_DeletesExactlyWhatWasReceivedBeforeTheCutoff_RecordsWithTheirReport()
    {
        var cutoff = DmarcRetention.CutoffOf(Now);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = Store(db);
        var driver = new DmarcIngestionDriver(store);
        await driver.StartAsync(cutoff.AddDays(-3));
        await driver.StoreAsync(Accepted("expired-1", cutoff.AddMicroseconds(-1), [Record("192.0.2.1", 1), Record("192.0.2.2", 1)]));
        await driver.StoreAsync(Accepted("kept-1", cutoff, [Record("192.0.2.3", 1), Record("192.0.2.4", 1), Record("192.0.2.5", 1)]));
        var expiredClaim = await driver.ClaimAsync(cutoff.AddDays(-1));
        await store.SetOutcomeAsync(driver.Epoch, expiredClaim, DmarcMessageOutcome.RetryPending, Ct);
        await store.RecordAttemptAsync(Now, DmarcRunOutcome.Succeeded, Ct);
        var state = await store.ReadStateAsync(Ct);

        var first = await new SqlDmarcReportRetention(db).DeleteReceivedBeforeAsync(cutoff, Ct);

        first.ShouldBe(new DmarcRetentionResult(Reports: 1, Messages: 2));
        (await ReadStringsAsync(fixture.ConnectionString, "SELECT reporter_report_id FROM dmarc_reports"))
            .ShouldBe(["kept-1"], "a report received at the cutoff is kept");
        (await ReadStringsAsync(fixture.ConnectionString, "SELECT count(*)::text FROM dmarc_report_rows"))
            .ShouldBe(["3"], "the expired report's records went with it");
        (await ReadStringsAsync(fixture.ConnectionString, "SELECT count(*)::text FROM dmarc_mailbox_messages"))
            .ShouldBe(["1"]);
        (await new SqlDmarcReportRetention(db).DeleteReceivedBeforeAsync(cutoff, Ct))
            .ShouldBe(new DmarcRetentionResult(0, 0), "a second run takes nothing more");
        (await store.ReadStateAsync(Ct)).ShouldBe(state, "the state row holds no personal data and is never deleted");
    }
}
