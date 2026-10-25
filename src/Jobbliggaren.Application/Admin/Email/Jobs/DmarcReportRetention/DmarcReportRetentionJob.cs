using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportRetention;

/// <summary>
/// #1981 PR 4a (ADR 0185) — deletes every DMARC report and ledger row received before <see cref="DmarcRetention.CutoffOf"/>,
/// the cutoff ingestion skips by, so neither keeps what the other drops. A report lives at most twelve calendar months
/// from receipt plus the time to the next successful run. A failed run leaves the rows for the next one. Logs counts only.
/// </summary>
public sealed partial class DmarcReportRetentionJob(
    IDmarcReportRetention retention,
    IDateTimeProvider clock,
    ILogger<DmarcReportRetentionJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var deleted = await retention.DeleteReceivedBeforeAsync(DmarcRetention.CutoffOf(clock.UtcNow), cancellationToken);
        LogDeleted(deleted.Reports, deleted.Messages);
    }

    [LoggerMessage(3124, LogLevel.Information,
        "[DmarcReportRetention] Deleted {Reports} reports and {Messages} ledger rows received before the retention cutoff")]
    private partial void LogDeleted(int reports, int messages);
}
