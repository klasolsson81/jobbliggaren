using Hangfire;
using Hangfire.States;
using Hangfire.Storage;
using Jobbliggaren.Application.Admin.BackgroundJobs;

namespace Jobbliggaren.Api.BackgroundJobs;

/// <summary>
/// Api-composition-root implementation of <see cref="IBackgroundJobController"/>
/// (#204 / TD-83 PR2). Wraps Hangfire's <see cref="IRecurringJobManager"/> /
/// <see cref="IBackgroundJobClient"/> / storage so Application stays Hangfire-free
/// (dotnet-architect bind, parity with the read-side endpoints which also keep
/// Hangfire in the Api). All Hangfire types are confined here; only BCL values
/// (a job-id string, the <see cref="RequeueOutcome"/> enum) cross the port.
///
/// The Hangfire client + storage are registered by <c>AddHangfire(...)</c> in
/// Program.cs (the Api is a storage-only client; the Worker process runs the
/// HangfireServer that actually executes the enqueued work).
///
/// Hangfire's client/storage API (<c>Trigger</c>, <c>GetConnection</c>,
/// <c>GetStateData</c>, <c>Requeue</c>) is synchronous and exposes no
/// CancellationToken overload, so the port's <c>ct</c> parameter is intentionally
/// not propagated into Hangfire here — the operations are fast, non-blocking
/// enqueue/storage calls.
/// </summary>
internal sealed class HangfireBackgroundJobController(
    IRecurringJobManager recurringJobs,
    IBackgroundJobClient backgroundJobs,
    JobStorage storage) : IBackgroundJobController
{
    public Task<string> TriggerRecurringAsync(string recurringJobId, CancellationToken ct)
    {
        // Allowlist membership is enforced by the command validator before this runs.
        // Trigger enqueues an ad-hoc run of the recurring job against shared storage;
        // the Worker's HangfireServer picks it up. Returns void → echo the id.
        recurringJobs.Trigger(recurringJobId);
        return Task.FromResult(recurringJobId);
    }

    public Task<RequeueOutcome> RequeueAsync(string jobId, CancellationToken ct)
    {
        // GetStateData is the canonical current-state lookup (no history-ordering
        // assumption): null = no such job; .Name = the current state name.
        using var connection = storage.GetConnection();
        var stateData = connection.GetStateData(jobId);

        if (stateData is null)
            return Task.FromResult(RequeueOutcome.JobNotFound);

        if (!string.Equals(stateData.Name, FailedState.StateName, StringComparison.Ordinal))
            return Task.FromResult(RequeueOutcome.NotInFailedState);

        backgroundJobs.Requeue(jobId);
        return Task.FromResult(RequeueOutcome.Requeued);
    }
}
