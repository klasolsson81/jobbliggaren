using Hangfire.PostgreSql;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// #688 — builds the Worker's <see cref="PostgreSqlStorageOptions"/>. Lifted to a static factory
/// (parity <see cref="HangfireConnectionStringResolver"/>, itself lifted for testability) so the
/// load-bearing <see cref="PostgreSqlStorageOptions.UseSlidingInvisibilityTimeout"/> flag is
/// assertable without booting Hangfire (CLAUDE.md §2.4).
/// <para>
/// Sliding invisibility: without it, a fetched job's <c>fetchedat</c> is stamped ONCE and the job
/// becomes re-fetchable after <c>InvisibilityTimeout</c> (default 30 min) EVEN WHILE STILL RUNNING —
/// the SCB population (~1.5–3 h) was re-fetched at ~29.5 min ceilings on the first live run
/// (8 starts / 0 completions). With sliding, Hangfire.PostgreSql's heartbeat process re-stamps
/// <c>fetchedat</c> every <c>InvisibilityTimeout / 5</c> (= 6 min) while the worker is processing,
/// so a long job keeps its fetch lease; crash recovery is unchanged (heartbeats stop → the job is
/// visible again after 30 min). Verified against pinned Hangfire.PostgreSql 1.21.1 source
/// (PostgreSqlFetchedJob). Also protects SyncPlatsbankenSnapshotWorker (tens of minutes).
/// </para>
/// <para>
/// #693 — <see cref="PostgreSqlStorageOptions.DistributedLockTimeout"/> raised to 12 h. The
/// <c>[DisableConcurrentExecution]</c> mutex is a <c>PostgreSqlDistributedLock</c> whose row carries
/// a single <c>acquired</c> timestamp with NO heartbeat renewal (unlike the fetch lease above), so a
/// held lock is takeover-able at <c>acquired + DistributedLockTimeout</c>. At the 10-min default the
/// mutex was SOFT for every job holding its lock longer than 10 min — during the #688 live run a
/// duplicate SCB job took over the lock at exactly +10:00 and co-ran with the ~11 h population.
/// Unlike the InvisibilityTimeout case (#688 Q3 rejected a global raise because a scoped heartbeat
/// existed), the storage exposes NO per-resource timeout and NO scoped renewal — the softness is
/// fleet-wide, so the global knob sized to the longest real hold time (~11 h) IS the correct fix.
/// </para>
/// </summary>
public static class HangfireStorageOptionsFactory
{
    public static PostgreSqlStorageOptions Create(bool prepareSchemaIfNecessary) => new()
    {
        SchemaName = "hangfire",
        PrepareSchemaIfNecessary = prepareSchemaIfNecessary,
        UseSlidingInvisibilityTimeout = true,
        // Pinned explicitly (dotnet-architect #688): crash-recovery latency AND the sliding
        // heartbeat cadence (InvisibilityTimeout / 5 = 6 min) both derive from this value —
        // load-bearing properties that must not float on a package default.
        InvisibilityTimeout = TimeSpan.FromMinutes(30),
        // #693 — the DisableConcurrentExecution distributed lock has NO heartbeat renewal
        // (Hangfire.PostgreSql 1.21.1 PostgreSqlDistributedLock: the lock row's `acquired` is
        // stamped once, expiry SQL is DELETE ... WHERE acquired < now - DistributedLockTimeout).
        // A held lock is stealable at acquired + this timeout, so at the 10-min default the mutex
        // was soft for any job holding >10 min — a duplicate SCB job took over the lock at exactly
        // +10:00 during the #688 live run. 12 h covers the real ~11 h SCB runtime (a full ~1.17M-row
        // re-fetch at 6/10 s EVERY run, incl. the weekly refresh) so a duplicate can no longer take
        // over mid-run. Global storage-level value — must not float on the package default.
        DistributedLockTimeout = TimeSpan.FromHours(12),
    };
}
