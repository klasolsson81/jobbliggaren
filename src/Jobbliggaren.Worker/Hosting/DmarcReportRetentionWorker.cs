using Hangfire;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportRetention;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>#1981 PR 4a (ADR 0183) — the daily twelve-month retention of the stored DMARC reports.</summary>
public sealed class DmarcReportRetentionWorker(DmarcReportRetentionJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
