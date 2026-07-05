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
/// </summary>
public sealed class ScbCompanyRegisterSyncWorker(IScbCompanyRegisterRefresher refresher)
{
    [DisableConcurrentExecution(timeoutInSeconds: 4 * 60 * 60)]
    public Task RunAsync(CancellationToken cancellationToken) => refresher.RefreshAsync(cancellationToken);
}
