using System.Diagnostics;
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

    [Fact]
    public async Task RefreshAsync_AnalyzesTheRegister_SoThePlannerIsNotBlindAfterTheBulkLoad()
    {
        // #560 / ADR 0119 — the ANALYZE-after-bulk-load guard (CLAUDE.md §3.6). The register was
        // populated to 1 066 938 rows with ZERO rows in pg_stats, and the planner then walked the wrong
        // index at 1 681-6 268 ms. Autovacuum cannot be the guarantee: its trigger is change-driven and
        // those counters are discarded on an unclean shutdown, leaving a write-once/read-only table with
        // nothing left to re-arm it. So the sync must ANALYZE explicitly, and this pins that it does.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        var before = await ReadAnalyzeStatsAsync(ct);

        // TWO batches, so "once per completed run" is a claim the counter can actually test: a per-batch
        // ANALYZE — the anti-pattern RB-3.1 rejects by name — would land AnalyzeCount at +2.
        var run = await BuildRefresher(
            new FakeSource([Legal("5560000501")], fetched: 1000, extraBatches: [[Legal("5560000502")]]), T0)
            .RefreshAsync(ct);

        run.RowsUpserted.ShouldBe(2);

        var after = await ReadAnalyzeStatsAsync(ct);
        after.LastAnalyzeEpoch.ShouldBeGreaterThan(before.LastAnalyzeEpoch,
            "en lyckad SCB-synk måste uppdatera planerarstatistiken för company_register");
        after.AnalyzeCount.ShouldBe(before.AnalyzeCount + 1,
            "exakt EN ANALYZE per körning — aldrig en per batch (RB-3.1)");
        after.VacuumCount.ShouldBe(before.VacuumCount,
            "vanlig ANALYZE, aldrig VACUUM ANALYZE — vakuum är autovacuums ärende (RB-3.1)");

        // The claim is NOT "an ANALYZE happened" — that is what the counters above say, and a
        // column-scoped ANALYZE would satisfy them while leaving the search columns blind, which is
        // verbatim the #560 defect. This is the claim: the columns the register search plans against
        // carry statistics. ANALYZE on a corpus this small still produces a row per column (measured,
        // postgres:18 2026-07-25), and ResetAsync cleared pg_statistic so nothing here is inherited.
        var analysed = await ReadAnalysedColumnsAsync(ct);
        analysed.ShouldContain("company_name");        // #560 — the name-prefix search
        analysed.ShouldContain("organization_number"); // the org.nr lookup
        analysed.ShouldContain("status");              // the sweep + browse predicate
        analysed.ShouldContain("sate_kommun_code");    // the kommun axis
        analysed.ShouldContain("sni_codes");           // the SNI axis
    }

    [Fact]
    public async Task RefreshAsync_NarratesTheAnalyzeStep_AfterTheRunSummary()
    {
        // Two claims, both about the run log. (1) Without EventId 5718 the ANALYZE is invisible in a run
        // that is otherwise fully narrated, so an operator diagnosing a slow register search cannot tell
        // whether the last sync refreshed the planner's statistics. (2) The summary (5712) is emitted
        // BEFORE the ANALYZE, so a failing ANALYZE costs the run its statistics and not its numbers —
        // the operator deciding whether to re-spend an ~11 h metered extract keeps the figures either
        // way. That is the whole guarantee, not a proxy for it: it is an ordering property of the happy
        // path, which is exactly what this assertion measures (senior-cto-advisor 2026-07-25).
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        var logger = new CapturingLogger<ScbCompanyRegisterRefresher>();
        await BuildRefresher(new FakeSource([Legal("5560000511")], fetched: 1000), T0, logger)
            .RefreshAsync(ct);

        var ids = logger.Entries.Select(e => e.EventId.Id).ToList();
        // Both presence checks are load-bearing: IndexOf returns -1 for a missing id, and -1 < n would
        // pass vacuously on a suite where either line had been deleted.
        ids.ShouldContain(5712);
        ids.ShouldContain(5718);
        ids.IndexOf(5712).ShouldBeLessThan(ids.IndexOf(5718),
            "körningens sammanfattning måste loggas FÖRE ANALYZE-steget: flyttas den under anropet "
            + "förlorar en misslyckad ANALYZE hela sifferraden ur körningsloggen");
    }

    [Fact]
    public async Task RefreshAsync_AnalyzesAfterTheSweep_SoStatisticsDescribeTheStatusTheRunLeftBehind()
    {
        // The ORDERING half, and it is not decoration: the sweep rewrites `status` across the untouched
        // majority, so statistics collected before it describe a distribution the run then discards.
        // pg_stats is the oracle that can tell the two orderings apart — after a run that deregistered
        // every row, the planner's most-common-value list for `status` must say Deregistered. It says
        // Active both if the ANALYZE ran too early AND if it never ran at all (run 1's statistics
        // survive), so this claim catches the ordering defect and the removal defect independently.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        // Run 1 (T0): 50 Active rows. No prior baseline → sweep applies; the run's own fresh rows
        // survive (synced_at == runStartedAt, not < it). Its ANALYZE leaves pg_stats saying "Active".
        var seeded = Enumerable.Range(1, 50).Select(i => Legal($"55600006{i:D2}")).ToList();
        await BuildRefresher(new FakeSource(seeded, fetched: 1000), T0).RefreshAsync(ct);
        (await ReadStatusMostCommonValsAsync(ct)).ShouldContain("Active"); // the pre-state is real

        // Run 2 (T1): touches nothing, fetched 1000 ≥ 0.80 × 1000 → the sweep applies and flips all 50.
        var run2 = await BuildRefresher(new FakeSource([], fetched: 1000), T1).RefreshAsync(ct);
        run2.SweepApplied.ShouldBeTrue();
        run2.RowsDeregistered.ShouldBe(50);

        // These two assertions are load-bearing AS A PAIR and must not be reordered or split: line 1
        // demands "Deregistered" present, line 2 demands "Active" absent, and no single static
        // statistics snapshot can satisfy both — so the pair PROVES a refresh happened between run 1
        // and here, whatever any neighbour left behind. (An empty MCV yields zero rows from unnest and
        // fails the first assertion, so the small-sample hazard cannot produce a false pass either.)
        var mcv = await ReadStatusMostCommonValsAsync(ct);
        mcv.ShouldContain("Deregistered",
            "statistiken måste beskriva tabellen EFTER sweepen, inte före");
        mcv.ShouldNotContain("Active"); // no row is Active any more — stale statistics would still say so
    }

    [Fact]
    public async Task RefreshAsync_DoesNotAnalyze_WhenTheSyncIsDisabled()
    {
        // The counterweight against over-firing (parity the browse-all claim in the plan guard): the
        // disabled path loads nothing, so it has no table to ANALYZE. This is what goes red the day the
        // call is hoisted above the Enabled check.
        var ct = TestContext.Current.CancellationToken;
        await ResetAsync(ct);

        var before = await ReadAnalyzeStatsAsync(ct);

        var run = await BuildRefresher(
            new FakeSource([Legal("5560000601")], fetched: 1000), T0, enabled: false).RefreshAsync(ct);

        run.SweepSkipReason.ShouldBe("disabled");

        // A single read suffices — last_analyze and analyze_count are written synchronously, so an
        // ANALYZE that had happened would already be visible here (see ReadAnalyzeStatsAsync).
        var after = await ReadAnalyzeStatsAsync(ct);
        after.LastAnalyzeEpoch.ShouldBe(before.LastAnalyzeEpoch,
            "en avstängd körning laddar ingenting och ska inte röra statistiken");
        after.AnalyzeCount.ShouldBe(before.AnalyzeCount);
    }

    private ScbCompanyRegisterRefresher BuildRefresher(
        IScbCompanyRegisterSource source, DateTimeOffset now,
        ILogger<ScbCompanyRegisterRefresher>? logger = null, bool enabled = true) =>
        new(source,
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(now),
            Options.Create(new ScbRegisterOptions { Enabled = enabled, FloorAbsolute = 1, FloorRelativeRatio = 0.80 }),
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
        // TRUNCATE does NOT clear pg_statistic, and neither does a following ANALYZE on the now-empty
        // table (do_analyze_rel skips update_attstats on an empty sample) — measured, postgres:18
        // 2026-07-25. Two neighbours in [Collection("Worker")] seed all-Active corpora into THIS
        // container and ANALYZE them (CompanyRegisterSearchQueryPlanTests, CompanyWatchBrowseQueryPlanTests),
        // so without this delete their leftovers satisfy both the column-coverage claim and test 2's
        // pre-state row. Safe for them: each does its own TRUNCATE → seed → ANALYZE.
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM pg_statistic WHERE starelid = 'public.company_register'::regclass;", ct);
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

    /// <summary>
    /// The maintenance counters for <c>company_register</c>: <c>last_analyze</c> as seconds since the
    /// epoch (0 when never analysed), plus the ANALYZE and VACUUM tallies.
    ///
    /// <para>
    /// <b>There is nothing to poll for.</b> An earlier version of this helper slept up to 20 s on the
    /// theory that these are deferred cumulative counters flushed at most once a second. They are not:
    /// <c>pgstat_report_analyze()</c> takes the shared-memory entry lock and writes
    /// <c>last_analyze_time</c> and <c>analyze_count</c> DIRECTLY when the command completes (PG15+
    /// shared-memory stats), unlike the pending counters in the same view (<c>numscans</c>,
    /// <c>n_tup_*</c>). Measured on postgres:18 2026-07-25: ANALYZE followed by an immediate read shows
    /// <c>analyze_count = 1</c>. The poll cost ~60 s of the class's ~70 s and guarded nothing.
    /// </para>
    ///
    /// <para>
    /// <c>pg_stat_clear_snapshot()</c> stays, for a DIFFERENT mechanism: <c>stats_fetch_consistency</c>
    /// defaults to <c>cache</c> (PG15+), so a transaction holds the snapshot it first read. Each read
    /// here is its own transaction, which already suffices — the call is insurance for the day these
    /// reads get wrapped in one, not the thing that makes them correct today.
    /// </para>
    ///
    /// <para>
    /// Reads the MANUAL columns, never <c>last_autoanalyze</c>/<c>autoanalyze_count</c>. That separation
    /// is what makes autovacuum unable to forge a pass here, and it is load-bearing — do not "simplify"
    /// this into <c>GREATEST(last_analyze, last_autoanalyze)</c>. Epoch double rather than
    /// <c>timestamptz</c> keeps Npgsql's infinity/nullable scalar mapping out of an assertion that only
    /// needs "later than".
    /// </para>
    /// </summary>
    private async Task<(double LastAnalyzeEpoch, long AnalyzeCount, long VacuumCount)>
        ReadAnalyzeStatsAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot();", ct);
        var rows = await db.Database.SqlQueryRaw<AnalyzeStatsRow>(
            """
            -- Aliases are snake_case: the DbContext's naming convention applies to query types too, so
            -- EF looks for last_analyze_epoch / analyze_count / vacuum_count, not the PascalCase
            -- property names.
            SELECT COALESCE(EXTRACT(EPOCH FROM last_analyze), 0)::double precision AS last_analyze_epoch,
                   COALESCE(analyze_count, 0) AS analyze_count,
                   COALESCE(vacuum_count, 0) AS vacuum_count
            FROM pg_stat_user_tables
            WHERE schemaname = 'public' AND relname = 'company_register'
            """).ToListAsync(ct);
        return rows.Count == 0 ? (0, 0, 0) : (rows[0].LastAnalyzeEpoch, rows[0].AnalyzeCount, rows[0].VacuumCount);
    }

    private sealed record AnalyzeStatsRow(double LastAnalyzeEpoch, long AnalyzeCount, long VacuumCount);

    /// <summary>
    /// The columns of <c>company_register</c> that currently carry planner statistics.
    ///
    /// <para>
    /// This is the oracle that separates "an ANALYZE happened" from "the search columns have
    /// statistics", and only the second one is the claim. A column-scoped
    /// <c>ANALYZE company_register (status)</c> bumps <c>last_analyze</c> AND <c>analyze_count</c>
    /// (measured, postgres:18 2026-07-25) while leaving <c>company_name</c> and
    /// <c>organization_number</c> with no statistics at all — which is verbatim the #560 defect.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<string>> ReadAnalysedColumnsAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.SqlQueryRaw<string>(
            """
            SELECT attname AS "Value"
            FROM pg_stats
            WHERE schemaname = 'public' AND tablename = 'company_register'
            """).ToListAsync(ct);
    }

    // The planner's most-common-value list for `status` — pg_stats, i.e. the statistics the planner
    // actually reads, not the activity counters. Empty when the column has no statistics yet.
    private async Task<IReadOnlyList<string>> ReadStatusMostCommonValsAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Database.SqlQueryRaw<string>(
            """
            SELECT unnest(most_common_vals::text::text[]) AS "Value"
            FROM pg_stats
            WHERE schemaname = 'public' AND tablename = 'company_register' AND attname = 'status'
            """).ToListAsync(ct);
        return rows;
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
        int partitionRequestFailures = 0,
        IReadOnlyList<IReadOnlyList<ScbCompanyRecord>>? extraBatches = null) : IScbCompanyRegisterSource
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

            // Lets a test drive MORE than one batch, so "ANALYZE once per completed run" becomes a
            // claim the analyze_count assertion can distinguish from "ANALYZE per batch".
            foreach (var extra in extraBatches ?? [])
                yield return extra;
        }
    }
}
