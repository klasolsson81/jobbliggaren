using Hangfire;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportIngestion;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// #1981 PR 4a (ADR 0185) — the hourly DMARC report ingestion. Never retried by Hangfire: the next hourly run is the
/// retry, and an automatic retry would repeat a refused login against a mailbox whose lockout policy is unmeasured.
/// </summary>
public sealed class DmarcReportIngestionWorker(DmarcReportIngestionJob job)
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: DmarcIngestionLimits.ConcurrencyTimeoutSeconds)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
