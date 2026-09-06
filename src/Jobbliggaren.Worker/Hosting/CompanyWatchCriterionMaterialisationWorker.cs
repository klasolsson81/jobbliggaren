using Hangfire;
using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// #1681 (ADR 0139) — Worker wrapper for the criterion-membership materialisation
/// (<see cref="ICompanyWatchCriterionMaterialiser"/>), applying Hangfire's filter attributes without
/// leaking Hangfire into the Application/Infrastructure layers (Clean Arch — ADR 0023 delbeslut 2;
/// parity <see cref="ScbCompanyRegisterSyncWorker"/> and <see cref="CompanyWatchScanWorker"/>).
///
/// <para>
/// <b><c>DisableConcurrentExecution</c>, 15-minute acquisition wait.</b> Two concurrent runs would
/// interleave a criterion's DELETE with the other run's INSERT and could leave a member set that is
/// the union of two resolutions, or a state row whose <c>member_count</c> does not match the rows
/// present — the exact disagreement the store's single transaction exists to prevent WITHIN a run.
/// The timeout is the ACQUISITION wait, not the hold time: measured, a full run is seconds
/// (docs/reviews/2026-09-06-1681-membership-measurement.md), so 15 minutes is a very large multiple of
/// the hold and a duplicate that waits that long is evidence something is genuinely wrong rather than
/// merely slow.
/// </para>
///
/// <para>
/// <b>Hangfire's DEFAULT retry is kept, deliberately — the opposite call to
/// <see cref="ScbCompanyRegisterSyncWorker"/>'s <c>AutomaticRetry(Attempts = 0)</c>, and the contrast
/// is the reasoning.</b> That job suppresses retries because each attempt re-spends an ~11 h metered
/// external call budget against a third party, so a retry storm is expensive and visible to SCB. This
/// job makes no external call, costs seconds, and is idempotent by construction (every criterion is
/// fully recomputed and REPLACED, never appended), so a retry is cheap and strictly reduces staleness.
/// Suppressing retries here would buy nothing and would turn a transient DB blip into a full day of
/// stale membership.
/// </para>
/// </summary>
public sealed class CompanyWatchCriterionMaterialisationWorker(
    ICompanyWatchCriterionMaterialiser materialiser)
{
    [DisableConcurrentExecution(timeoutInSeconds: 15 * 60)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        materialiser.MaterialiseAsync(cancellationToken);
}
