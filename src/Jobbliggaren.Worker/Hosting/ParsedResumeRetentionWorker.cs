using Hangfire;
using Jobbliggaren.Application.Resumes.Jobs.ParsedResumeRetention;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Worker-wrapper för <see cref="ParsedResumeRetentionJob"/> (TD-111) som applicerar Hangfire
/// <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka Hangfire-beroendet till
/// Application-lagret (Clean Arch — ADR 0023 delbeslut 2; paritet <see cref="StrandedMatchReaperWorker"/>).
/// Set-based ExecuteDelete-svep; idempotent (en utebliven körning plockas av nästa).
/// </summary>
public sealed class ParsedResumeRetentionWorker(ParsedResumeRetentionJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunAsync(CancellationToken cancellationToken) => job.RunAsync(cancellationToken);
}
