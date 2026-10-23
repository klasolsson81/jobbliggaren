using Hangfire;
using Jobbliggaren.Application.Admin.Email.Jobs.EmailSendCountRetention;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>#1981 PR 3 (ADR 0181) — the daily retention of our own send counts.</summary>
public sealed class EmailSendCountRetentionWorker(EmailSendCountRetentionJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
