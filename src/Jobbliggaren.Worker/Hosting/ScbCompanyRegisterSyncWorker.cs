using Hangfire;
using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// #560 (ADR 0091) — Worker wrapper for the SCB company-register refresh
/// (<see cref="IScbCompanyRegisterRefresher"/>), applying Hangfire's
/// <see cref="DisableConcurrentExecutionAttribute"/> without leaking Hangfire into the Application/
/// Infrastructure layers (Clean Arch — ADR 0023 delbeslut 2; parity <see cref="CompanyWatchScanWorker"/>).
/// <para>
/// Timeout 4 h (14400 s): the full population is ~1–3 h under the SCB 10-calls/10-s throttle. The
/// refresh must NEVER overlap itself — an <c>AutomaticRetry</c> overlap would run two 1–3 h extracts
/// against the same throttle budget and race the deregister sweep's per-run <c>synced_at</c> watermark.
/// </para>
/// <para>
/// #688 — <c>AutomaticRetry(Attempts = 0)</c>: a failed run goes straight to the Failed state (visible
/// in the admin failed-jobs list), NEVER a silent from-zero restart. Hangfire's default (10 attempts,
/// exponential backoff) re-runs the whole ~2 h extract per attempt, re-spending ~8k metered SCB calls
/// each time (first live run 2026-07-05: 8 starts / 0 completions). The upsert is idempotent — recovery
/// is the next weekly cron or a manual admin re-trigger, both deliberate. SCB-only: the other workers'
/// default retry behavior is a conscious ADR 0032 decision, out of scope here.
/// </para>
/// </summary>
public sealed class ScbCompanyRegisterSyncWorker(IScbCompanyRegisterRefresher refresher)
{
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [DisableConcurrentExecution(timeoutInSeconds: 4 * 60 * 60)]
    public Task RunAsync(CancellationToken cancellationToken) => refresher.RefreshAsync(cancellationToken);
}
