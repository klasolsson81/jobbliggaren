using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportRetention;
using Jobbliggaren.Application.UnitTests.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — the retention deletes by the same cutoff ingestion skips by, and logs counts only.
/// </summary>
public sealed class DmarcReportRetentionJobTests
{
    private sealed class RecordingRetention : IDmarcReportRetention
    {
        public List<DateTimeOffset> Cutoffs { get; } = [];

        public ValueTask<DmarcRetentionResult> DeleteReceivedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
        {
            Cutoffs.Add(cutoff);
            return ValueTask.FromResult(new DmarcRetentionResult(Reports: 4, Messages: 9));
        }
    }

    [Theory]
    [InlineData("2026-10-24T04:20:00Z", "2025-10-24T04:20:00Z")]
    [InlineData("2028-02-29T04:20:00Z", "2027-02-28T04:20:00Z")]
    [InlineData("2027-03-29T04:20:00Z", "2026-03-29T04:20:00Z")]
    public async Task RunAsync_DeletesWhatWasReceivedBeforeTheCutoffIngestionSkipsBy(string now, string cutoff)
    {
        var retention = new RecordingRetention();
        var at = DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture);
        var log = new CapturingLogger<DmarcReportRetentionJob>();

        await new DmarcReportRetentionJob(retention, new SettableClock(at), log).RunAsync(TestContext.Current.CancellationToken);

        retention.Cutoffs.ShouldBe([DateTimeOffset.Parse(cutoff, System.Globalization.CultureInfo.InvariantCulture)]);
        retention.Cutoffs[0].ShouldBe(DmarcRetention.CutoffOf(at));
        log.Records.ShouldHaveSingleItem().ShouldBe((
            Microsoft.Extensions.Logging.LogLevel.Information,
            3124,
            "[DmarcReportRetention] Deleted 4 reports and 9 ledger rows received before the retention cutoff"));
    }
}
