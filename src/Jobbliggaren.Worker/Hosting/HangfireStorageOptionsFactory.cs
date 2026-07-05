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
    };
}
