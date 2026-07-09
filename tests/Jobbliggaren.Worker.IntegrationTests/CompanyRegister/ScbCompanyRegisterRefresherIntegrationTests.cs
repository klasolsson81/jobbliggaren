using System.Runtime.CompilerServices;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — Testcontainers test for the orchestrator's real write path against Postgres. The
/// orchestrator resolves the CONCRETE <see cref="ScbCompanyRegisterStore"/> from child scopes (no
/// port — Fork 2), so its wiring (filter → upsert → floor-gated sweep → audit + the timestamp coupling
/// + the relative-floor read from prior audit rows) is only reachable through real DB. A fake source
/// feeds controlled batches; an injected fixed clock makes the timestamps deterministic.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class ScbCompanyRegisterRefresherIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset T0 = new(2026, 7, 4, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddDays(7);

    [Fact]
    public async Task RefreshAsync_ExcludesPnr_KeepsOwnFreshRows_ThenRelativeFloorSkipsSweep()
    {
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        // --- Run 1: clean, fetched 1000 ≥ absolute floor, no prior baseline → sweep APPLIES ---
        var run1 = await BuildRefresher(
            new FakeSource([Legal("5560000078"), PnrShaped()], fetched: 1000), T0).RefreshAsync(ct);

        run1.RowsExcludedPersonnummerShaped.ShouldBe(1); // the GDPR guard fired end-to-end
        run1.RowsUpserted.ShouldBe(1);
        run1.SweepApplied.ShouldBeTrue();
        var afterRun1 = await ReadAllAsync(ct);
        afterRun1.ShouldHaveSingleItem().OrganizationNumber.ShouldBe("5560000078"); // pnr NEVER persisted
        // Timestamp coupling: the sweep uses runStartedAt == the just-stamped synced_at, so this run's
        // own fresh row (synced_at == runStartedAt, not < it) is never swept.
        afterRun1[0].Status.ShouldBe(CompanyRegisterStatus.Active);

        // --- Run 2: fetched 100 < 0.80 × 1000 → relative floor SKIPS the sweep ---
        var run2 = await BuildRefresher(
            new FakeSource([Legal("5560000078")], fetched: 100), T1).RefreshAsync(ct);

        run2.SweepApplied.ShouldBeFalse();
        // Proves GetMaxObservedTotalRowsFetchedAsync read run 1's TotalRowsFetched=1000 from audit_log
        // (the auditor-serialization ↔ store-read contract end-to-end).
        run2.SweepSkipReason.ShouldBe("below-relative-floor");
        (await ReadAllAsync(ct)).ShouldAllBe(e => e.Status == CompanyRegisterStatus.Active); // no false deregistration
    }

    [Fact]
    public async Task RefreshAsync_ThreadsProtectedPartitionsToSweep_AndAuditsCount()
    {
        // #640 (Guard 1) end-to-end through the orchestrator: a run reporting an over-cap (0180, 70100)
        // protected partition still runs the sweep, but excludes just that key-space — the stale 62010 row
        // deregisters while the protected 70100 row is spared — and the protected count reaches BOTH the
        // result and the CompanyRegisterSynced audit payload.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        // Run 1 (T0): two Active rows in kommun 0180 — one under SNI 70100, one under 62010.
        await BuildRefresher(
            new FakeSource([Legal("5560000201", sni: "70100"), Legal("5560000202", sni: "62010")], fetched: 1000), T0)
            .RefreshAsync(ct);

        // Run 2 (T1): re-touches neither (both go stale) but reports the (0180, 70100) tail as protected.
        var run2 = await BuildRefresher(
            new FakeSource([], fetched: 1000,
                protectedPartitions: [(new ScbProtectedPartition("0180", "70100"), 2809)]), T1)
            .RefreshAsync(ct);

        run2.SweepApplied.ShouldBeTrue();
        run2.ProtectedPartitionCount.ShouldBe(1);
        var rows = await ReadAllAsync(ct);
        rows.Single(r => r.OrganizationNumber == "5560000201").Status.ShouldBe(CompanyRegisterStatus.Active);       // protected
        rows.Single(r => r.OrganizationNumber == "5560000202").Status.ShouldBe(CompanyRegisterStatus.Deregistered); // swept
        (await ReadLastProtectedCountFromAuditAsync(ct)).ShouldBe(1);   // ProtectedPartitionCount reached the audit row
    }

    [Fact]
    public async Task RefreshAsync_SkipsSweep_AndLogsGap_OnReconciliationGap()
    {
        // #640 (Guard 2) end-to-end: a run reporting a no-SNI reconciliation gap latches truncated → the
        // sweep is SKIPPED (a stale row is never deregistered) and the distinct gap warning (EventId 5714)
        // fires.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        // Run 1 (T0): one clean Active row (its own fresh row survives the run-1 sweep).
        await BuildRefresher(new FakeSource([Legal("5560000301")], fetched: 1000), T0).RefreshAsync(ct);

        // Run 2 (T1): reports a reconciliation gap → truncated → sweep skipped, gap logged.
        var logger = new CapturingLogger<ScbCompanyRegisterRefresher>();
        var run2 = await BuildRefresher(
            new FakeSource([], fetched: 1000, reconciliationGaps: 1), T1, logger).RefreshAsync(ct);

        run2.SweepApplied.ShouldBeFalse();
        run2.SweepSkipReason.ShouldBe("truncated-or-errored");
        (await ReadAllAsync(ct)).ShouldAllBe(e => e.Status == CompanyRegisterStatus.Active); // gap → no deregistration
        logger.Entries.ShouldContain(e => e.EventId.Id == 5714);                             // distinct gap warning fired
        // #717 — no protected over-cap tail this run → the tail-sizing WARN (5717) stays SILENT
        // (the LOG-ONLY clean-run guard: emit only when ProtectedPartitions.Count > 0).
        logger.Entries.ShouldNotContain(e => e.EventId.Id == 5717);
    }

    [Fact]
    public async Task RefreshAsync_ThreadsFailedPartitionCountToAudit_WhenSourceReportsPartitionFailures()
    {
        // #708 end-to-end: two SCB-rejected partition requests latch the run truncated (sweep SKIPPED so
        // nothing is falsely deregistered) AND the count reaches the durable CompanyRegisterSynced audit
        // payload (FailedPartitionCount=2) — the audit row alone diagnoses a truncated run. The completion
        // line (EventId 5712) carries the same count so the run log is self-describing.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        // Run 1 (T0): one clean Active row so its own fresh row survives (no prior baseline → sweep applies).
        await BuildRefresher(new FakeSource([Legal("5560000401")], fetched: 1000), T0).RefreshAsync(ct);

        // Run 2 (T1): reports two partition-request failures → truncated → sweep skipped, count audited.
        var logger = new CapturingLogger<ScbCompanyRegisterRefresher>();
        var run2 = await BuildRefresher(
            new FakeSource([], fetched: 1000, partitionRequestFailures: 2), T1, logger).RefreshAsync(ct);

        run2.SweepApplied.ShouldBeFalse();
        run2.SweepSkipReason.ShouldBe("truncated-or-errored");
        (await ReadAllAsync(ct)).ShouldAllBe(e => e.Status == CompanyRegisterStatus.Active); // truncated → no deregistration
        (await ReadLastFailedPartitionCountFromAuditAsync(ct)).ShouldBe(2);                  // reached the audit row
        logger.Entries.ShouldContain(e => e.EventId.Id == 5712 && e.Message.Contains("failedPartitions=2")); // completion line
    }

    [Fact]
    public async Task RefreshAsync_LogsAggregatedTailSizing_WhenRunProtectsOverCapCells()
    {
        // #717 end-to-end: a run that protects over-cap 5-digit tails emits ONE aggregated 5717 WARN with
        // the total unfetched tail rows (Σ per-key count − cap × leaves) — #641 facet evidence at ZERO
        // extra SCB calls. Two over-cap Juridisk-form leaves under 0180×00000 (31000 + 5000, cap 2000 →
        // 36000 − 4000 = 32000) plus 0180×70100 (2809 → 809) ⇒ total 32809 across 2 distinct keys.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        var logger = new CapturingLogger<ScbCompanyRegisterRefresher>();
        var run = await BuildRefresher(
            new FakeSource([], fetched: 1000, protectedPartitions:
            [
                (new ScbProtectedPartition("0180", "00000"), 31000),
                (new ScbProtectedPartition("0180", "00000"), 5000),   // second over-cap leaf, SAME key
                (new ScbProtectedPartition("0180", "70100"), 2809),
            ]), T0, logger).RefreshAsync(ct);

        run.ProtectedPartitionCount.ShouldBe(2);   // two DISTINCT (kommun, SNI) keys

        var tail = logger.Entries.Single(e => e.EventId.Id == 5717);
        tail.Level.ShouldBe(LogLevel.Warning);
        tail.Message.ShouldContain("antal=2");
        tail.Message.ShouldContain("total otäckt svans≈32809 rader");           // Σ tails (upper bound), multi-leaf
        tail.Message.ShouldContain("övre gräns");                                // reported as an at-most figure (#628)
        tail.Message.ShouldContain("0180×00000:count=36000,leaves=2,tail=32000"); // biggest first, accumulated
        tail.Message.ShouldContain("Loggar aldrig org.nr");                       // §5 discipline on the new line
    }

    private ScbCompanyRegisterRefresher BuildRefresher(
        IScbCompanyRegisterSource source, DateTimeOffset now,
        ILogger<ScbCompanyRegisterRefresher>? logger = null) =>
        new(source,
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(now),
            Options.Create(new ScbRegisterOptions { Enabled = true, FloorAbsolute = 1, FloorRelativeRatio = 0.80 }),
            logger ?? NullLogger<ScbCompanyRegisterRefresher>.Instance);

    private static ScbCompanyRecord Legal(string orgNr, string sni = "29100") =>
        new(orgNr, "Acme AB", "0180", "Stockholm", [sni], false, "1");

    // 3rd digit '0' < '2' → personnummer-shaped → must be excluded end-to-end, never reaching the DB.
    private static ScbCompanyRecord PnrShaped() =>
        new("9001011234", "Anna Andersson", "0180", "Stockholm", [], false, "1");

    private async Task ResetAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE company_register;", ct);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM audit_log WHERE event_type = 'System.CompanyRegisterSynced';", ct);
    }

    private async Task<List<ScbCompanyRegisterEntry>> ReadAllAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<ScbCompanyRegisterEntry>().AsNoTracking()
            .OrderBy(e => e.OrganizationNumber).ToListAsync(ct);
    }

    // The ProtectedPartitionCount on the most recent CompanyRegisterSynced audit row — proves the #640
    // count survived auditor serialization into audit_log (parity the TotalRowsFetched round-trip above).
    private async Task<int?> ReadLastProtectedCountFromAuditAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var counts = await db.Database.SqlQueryRaw<int>(
            """
            SELECT (payload->>'ProtectedPartitionCount')::int AS "Value"
            FROM audit_log
            WHERE event_type = 'System.CompanyRegisterSynced'
            ORDER BY occurred_at DESC
            LIMIT 1
            """).ToListAsync(ct);
        return counts.Count == 0 ? null : counts[0];
    }

    // #708 — the FailedPartitionCount on the most recent CompanyRegisterSynced audit row: proves the
    // SCB-rejected-partition tally survived auditor serialization into audit_log (parity the
    // ProtectedPartitionCount / TotalRowsFetched round-trips above).
    private async Task<int?> ReadLastFailedPartitionCountFromAuditAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var counts = await db.Database.SqlQueryRaw<int>(
            """
            SELECT (payload->>'FailedPartitionCount')::int AS "Value"
            FROM audit_log
            WHERE event_type = 'System.CompanyRegisterSynced'
            ORDER BY occurred_at DESC
            LIMIT 1
            """).ToListAsync(ct);
        return counts.Count == 0 ? null : counts[0];
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    // Captures emitted log entries so a test can assert a specific LoggerMessage (by EventId) fired.
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }

    private sealed class FakeSource(
        IReadOnlyList<ScbCompanyRecord> batch,
        int fetched,
        IReadOnlyList<(ScbProtectedPartition Partition, int OverCapCount)>? protectedPartitions = null,
        int reconciliationGaps = 0,
        int partitionRequestFailures = 0) : IScbCompanyRegisterSource
    {
        public async IAsyncEnumerable<IReadOnlyList<ScbCompanyRecord>> StreamLegalEntitiesAsync(
            ScbSyncOutcome outcome, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            outcome.RecordCounted();
            outcome.RecordFetched(fetched); // drives the floor independently of the batch's row count
            // #717 — carry the over-cap count so the outcome accumulates per key (a repeated key mimics the
            // same (kommun, SNI) recorded by several over-cap Juridisk-form leaves).
            foreach (var (partition, overCapCount) in protectedPartitions ?? [])
                outcome.RecordProtectedPartition(partition.SeatMunicipalityCode, partition.SniCode, overCapCount);
            for (var i = 0; i < reconciliationGaps; i++)
                outcome.RecordReconciliationGap();
            for (var i = 0; i < partitionRequestFailures; i++)
                outcome.RecordPartitionRequestFailed(); // #708 — each latches truncated + tallies the audit count
            await Task.CompletedTask.ConfigureAwait(false);
            yield return batch;
        }
    }
}
