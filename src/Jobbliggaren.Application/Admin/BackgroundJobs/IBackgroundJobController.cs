namespace Jobbliggaren.Application.Admin.BackgroundJobs;

/// <summary>
/// Application port for the admin operator surface's Hangfire mutations (#204 /
/// TD-83 PR2): trigger a recurring job now, requeue a failed job. Implemented in
/// the Api composition root (<c>HangfireBackgroundJobController</c>) wrapping
/// Hangfire's <c>IRecurringJobManager</c> / <c>IBackgroundJobClient</c> /
/// <c>IMonitoringApi</c>, so Application stays Hangfire-type-free (CLAUDE.md §2.1).
///
/// All signatures are BCL-only — no Hangfire type crosses this boundary. The
/// requeue precondition (does the job exist, is it Failed) is resolved inside the
/// implementation (where Hangfire's monitoring API lives) and surfaced as the
/// BCL <see cref="RequeueOutcome"/> enum; the command handler maps that to a
/// <c>DomainError</c> (NotFound / Conflict).
/// </summary>
public interface IBackgroundJobController
{
    /// <summary>
    /// Triggers an immediate ad-hoc run of a recurring job. The caller is
    /// responsible for validating <paramref name="recurringJobId"/> against the
    /// closed <see cref="Jobbliggaren.Application.BackgroundJobs.RecurringJobIds"/>
    /// allowlist BEFORE calling this (fan-out/RCE prevention). Returns the
    /// recurring job id (echoed) for the audit/response.
    /// </summary>
    Task<string> TriggerRecurringAsync(string recurringJobId, CancellationToken ct);

    /// <summary>
    /// Requeues a failed background job only if it currently exists and is in the
    /// Failed state. Returns the outcome; the implementation never requeues a job
    /// in any other state.
    /// </summary>
    Task<RequeueOutcome> RequeueAsync(string jobId, CancellationToken ct);
}

/// <summary>
/// Outcome of a requeue attempt (BCL enum; the handler maps it to a DomainError).
/// </summary>
public enum RequeueOutcome
{
    /// <summary>The job existed, was Failed, and was requeued.</summary>
    Requeued,

    /// <summary>No job with that id exists in Hangfire storage → NotFound (404).</summary>
    JobNotFound,

    /// <summary>The job exists but is not in the Failed state → Conflict (409).</summary>
    NotInFailedState,
}
