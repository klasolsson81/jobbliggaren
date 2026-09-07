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
/// <b>Both methods share ONE distributed-lock resource, and that is a CORRECTNESS requirement, not
/// hygiene</b> (senior-cto-advisor, 2026-09-07). <c>DisableConcurrentExecution</c>'s <c>(int)</c>
/// constructor keys the lock <c>&lt;Type&gt;.&lt;Method&gt;</c>, so two different methods get two
/// different locks and do not exclude each other at all. Its second constructor takes an explicit
/// resource string, under which both methods resolve to one key — measured against Hangfire.Core
/// 1.8.24 by reflection, 2026-09-07.
/// </para>
///
/// <para>
/// <b>The hazard the shared key closes, precisely.</b> Two overlapping resolutions of ONE criterion —
/// the old predicate P1 giving members M1, the new P2 giving M2 — each run
/// <c>DELETE ... WHERE criterion_id = @c</c> under READ COMMITTED, so neither removes rows the other
/// has inserted but not yet committed; where the criterion had no members to begin with (a fresh
/// create, or a previous <c>TooBroad</c>) the DELETEs lock nothing at all. Both INSERTs then succeed
/// whenever <c>M1 ∩ M2 = ∅</c>, because the member table's composite PK
/// <c>(criterion_id, organization_number)</c> is the only thing that could collide. If the NEW
/// resolution commits last, the surviving state row carries fingerprint F2 while the member table
/// holds <c>M1 ∪ M2</c> — the read gate <c>criteria_fingerprint = @fingerprint</c> PASSES, and the
/// surface renders an exact, confident, WRONG number. Disjoint sets are what an ordinary edit
/// produces (swap the SNI axis wholesale and M1 ∩ M2 is empty), so this is not a tail case.
/// </para>
///
/// <para>
/// The hazard does not exist while there is a single writer, which is why part 1 did not need the
/// shared key. <b>Any second writer not under the same lock creates it</b> — so this argument, and
/// not tidiness, is why the resource string is here.
/// </para>
///
/// <para>
/// ⚠ <b>Lapse condition, named because nothing detects it automatically:</b> one shared key suffices
/// only for as long as the writer set is these two single-threaded recurring jobs. If a per-user
/// enqueue path is ever added, or if the sweep is ever given parallelism, this stops sufficing and
/// per-criterion locking becomes mandatory (senior-cto-advisor escalation E1, 2026-09-07).
/// </para>
///
/// <para>
/// <b>15-minute acquisition wait</b>, on both. The timeout is the ACQUISITION wait, not the hold
/// time: measured, a full run is seconds
/// (docs/reviews/2026-09-06-1681-membership-measurement.md), so 15 minutes is a very large multiple
/// of the hold and a duplicate that waits that long is evidence something is genuinely wrong rather
/// than merely slow.
/// </para>
/// </summary>
public sealed class CompanyWatchCriterionMaterialisationWorker(
    ICompanyWatchCriterionMaterialiser materialiser)
{
    /// <summary>
    /// The lock BOTH jobs take. A constant rather than two string literals, because two literals that
    /// must be equal are a magic string one edit away from silently un-sharing the lock (§5), and the
    /// failure would be invisible: every test stays green and the wrong-number hazard simply returns.
    /// </summary>
    private const string MaterialisationLock = "company-watch-criterion-materialisation";

    /// <summary>
    /// The nightly full recompute. <b>Hangfire's DEFAULT retry is kept, deliberately — the opposite
    /// call to <see cref="ScbCompanyRegisterSyncWorker"/>'s <c>AutomaticRetry(Attempts = 0)</c>, and
    /// the contrast is the reasoning.</b> That job suppresses retries because each attempt re-spends
    /// an ~11 h metered external call budget against a third party, so a retry storm is expensive and
    /// visible to SCB. This job makes no external call, costs seconds, and is idempotent by
    /// construction (every criterion is fully recomputed and REPLACED, never appended), so a retry is
    /// cheap and strictly reduces staleness. Suppressing retries here would buy nothing and would turn
    /// a transient DB blip into a full day of stale membership.
    /// </summary>
    [DisableConcurrentExecution(MaterialisationLock, 15 * 60)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        materialiser.MaterialiseAsync(cancellationToken);

    /// <summary>
    /// #1681 clause (ii) — the reconciling sweep that makes a user's OWN edit visible without waiting
    /// for the nightly run.
    ///
    /// <para>
    /// <b><c>AutomaticRetry(Attempts = 0)</c>, and here the reasoning inverts against
    /// <see cref="RunAsync"/> directly above.</b> That method keeps Hangfire's retry because its next
    /// scheduled attempt is up to 24 h away, so a transient blip would otherwise cost a full day of
    /// staleness. This one runs every minute: <b>the next tick IS the retry</b>, and it re-derives
    /// the same candidate set from committed state, so a Hangfire retry would only duplicate work the
    /// scheduler is about to do anyway — while queueing a retry behind the shared lock that the next
    /// tick then contends with. The sweep is stateless; a lost tick loses nothing.
    /// </para>
    /// </summary>
    [DisableConcurrentExecution(MaterialisationLock, 15 * 60)]
    [AutomaticRetry(Attempts = 0)]
    public Task SweepAsync(CancellationToken cancellationToken) =>
        materialiser.MaterialiseChangedAsync(cancellationToken);
}
