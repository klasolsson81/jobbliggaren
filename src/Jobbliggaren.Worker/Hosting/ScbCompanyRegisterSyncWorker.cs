using Hangfire;
using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// #560 (ADR 0091) — Worker wrapper for the SCB company-register refresh
/// (<see cref="IScbCompanyRegisterRefresher"/>), applying Hangfire's
/// <see cref="DisableConcurrentExecutionAttribute"/> without leaking Hangfire into the Application/
/// Infrastructure layers (Clean Arch — ADR 0023 delbeslut 2; parity <see cref="CompanyWatchScanWorker"/>).
/// <para>
/// Timeout 4 h (14400 s) is the ACQUISITION wait — how long a second execution blocks trying to get
/// the lock, not the hold time. The full population runs ~11 h (empirical, 2026-07-05→06: 665 min at
/// the 6-calls/10-s margin; every run is a full ~1.17M-row re-fetch, incl. the weekly refresh). The
/// refresh must NEVER overlap itself — an overlap runs two ~11 h extracts against the same throttle
/// budget and races the deregister sweep's per-run <c>synced_at</c> watermark. Actual mutual exclusion
/// is guaranteed by <c>DistributedLockTimeout = 12 h</c> (> the hold time) in
/// <see cref="HangfireStorageOptionsFactory"/> (#693) — a duplicate blocks 4 h, then lands Failed.
/// </para>
/// <para>
/// #688 — <c>AutomaticRetry(Attempts = 0)</c>: a failed run goes straight to the Failed state (visible
/// in the admin failed-jobs list), NEVER a silent from-zero restart. Hangfire's default (10 attempts,
/// exponential backoff) re-runs the whole ~11 h extract per attempt, re-spending the full metered SCB
/// call budget each time (first live run 2026-07-05: 8 starts / 0 completions). The upsert is
/// idempotent — recovery is the next weekly cron or a manual admin re-trigger, both deliberate.
/// SCB-only: the other workers' default retry behavior is a conscious ADR 0032 decision, out of scope
/// here.
/// </para>
/// </summary>
public sealed class ScbCompanyRegisterSyncWorker(IScbCompanyRegisterRefresher refresher)
{
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 4 * 60 * 60)]
    public Task RunAsync(CancellationToken cancellationToken) => refresher.RefreshAsync(cancellationToken);
}
