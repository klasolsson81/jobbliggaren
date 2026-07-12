using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — Testcontainers integration tests for <see cref="ScbCompanyRegisterStore"/> against
/// REAL Postgres (the bulk <c>jsonb_to_recordset ON CONFLICT</c> upsert, the <c>text[]</c> SNI column,
/// and the deregister sweep are Postgres-specific — never EF-InMemory). Each test resets BOTH tables the
/// store reads/writes (<c>company_register</c> AND the <c>System.CompanyRegisterSynced</c> audit rows the
/// floor-baseline read scans): the sweep is a whole-table operation and the baseline read is a
/// day-windowed <c>audit_log</c> scan, so a test must own both to be order-independent. The "Worker"
/// collection runs serially over ONE shared Testcontainers Postgres, so any sibling that writes a
/// <c>CompanyRegisterSynced</c> audit row (e.g. <c>ScbCompanyRegisterRefresherIntegrationTests</c>, which
/// records <c>TotalRowsFetched=1000</c>) would otherwise leak into the "no prior run" baseline assertion
/// (#685 — deterministically depending on suite order + the 90-day real-clock window).
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class ScbCompanyRegisterStoreTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddDays(7);
    private static readonly DateTimeOffset T2 = T0.AddDays(14);

    private static ScbCompanyRegisterEntry Entry(
        string orgNr,
        string name = "Acme AB",
        CompanyRegisterStatus status = CompanyRegisterStatus.Active,
        string municipality = "0180",
        params string[] sni) =>
        new()
        {
            OrganizationNumber = orgNr,
            Name = name,
            SeatMunicipalityCode = municipality,
            SeatMunicipalityName = "Stockholm",
            SniCodes = [.. sni],
            HasAdvertisingBlock = false,
            ScbStatusRaw = status == CompanyRegisterStatus.Active ? "1" : "9",
            Status = status,
        };

    [Fact]
    public async Task UpsertBatch_IsIdempotent_OneRowPerOrgNr_SyncedAtAdvances_CreatedAtStable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);
        var store = new ScbCompanyRegisterStore(ctx.Db);

        await store.UpsertBatchAsync([Entry("5560000012", name: "First")], T0, ct);
        await store.UpsertBatchAsync([Entry("5560000012", name: "Updated")], T1, ct);

        var rows = await ReadAllAsync(ctx.Db, ct);
        var row = rows.ShouldHaveSingleItem();
        row.Name.ShouldBe("Updated");
        row.SyncedAt.ShouldBe(T1);       // advanced on the second touch
        row.CreatedAt.ShouldBe(T0);      // unchanged since insert
    }

    [Fact]
    public async Task DeregisterMissing_FlipsStaleRowsToDeregistered_KeepsTouchedActive_NeverDeletes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);
        var store = new ScbCompanyRegisterStore(ctx.Db);

        // First run touches both at T0.
        await store.UpsertBatchAsync([Entry("5560000023"), Entry("5560000034")], T0, ct);
        // Second run (started at T1) touches only …23; …34 becomes stale (synced_at T0 < T1).
        await store.UpsertBatchAsync([Entry("5560000023")], T1, ct);

        var deregistered = await store.DeregisterMissingAsync(T1, [], ct);

        deregistered.ShouldBe(1);
        var rows = await ReadAllAsync(ctx.Db, ct);
        rows.Count.ShouldBe(2);                                              // nothing hard-deleted
        rows.Single(r => r.OrganizationNumber == "5560000023").Status.ShouldBe(CompanyRegisterStatus.Active);
        rows.Single(r => r.OrganizationNumber == "5560000034").Status.ShouldBe(CompanyRegisterStatus.Deregistered);
    }

    [Fact]
    public async Task DeregisterMissing_ProtectsOverCapPartitionTail_SweepsEverywhereElse()
    {
        // #640 (Guard 1): a dense-metro (kommun 0180, SNI 70100) tail was only partially fetched, so its
        // stale rows must be EXCLUDED from the sweep while every other stale row is still deregistered.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);
        var store = new ScbCompanyRegisterStore(ctx.Db);

        // First run (T0) touches all four.
        await store.UpsertBatchAsync(
        [
            Entry("5560000101", sni: "70100"),   // protected tail (0180 × 70100) — must survive the sweep
            Entry("5560000102", sni: "70100"),   // protected tail — must survive
            Entry("5560000103", sni: "62010"),   // same kommun, different SNI — swept
            Entry("5560000104", sni: "70100", municipality: "1480"),  // 70100 but different kommun — swept
        ], T0, ct);
        // Second run (T1) re-touches none of them → all four are stale (synced_at T0 < T1).
        var deregistered = await store.DeregisterMissingAsync(
            T1, [new ScbProtectedPartition("0180", "70100")], ct);

        deregistered.ShouldBe(2);   // only …103 and …104 flip; the (0180, 70100) pair is shielded
        var rows = await ReadAllAsync(ctx.Db, ct);
        rows.Single(r => r.OrganizationNumber == "5560000101").Status.ShouldBe(CompanyRegisterStatus.Active);
        rows.Single(r => r.OrganizationNumber == "5560000102").Status.ShouldBe(CompanyRegisterStatus.Active);
        rows.Single(r => r.OrganizationNumber == "5560000103").Status.ShouldBe(CompanyRegisterStatus.Deregistered);
        rows.Single(r => r.OrganizationNumber == "5560000104").Status.ShouldBe(CompanyRegisterStatus.Deregistered);
    }

    [Fact]
    public async Task DeregisterMissing_ProtectsRowCarryingProtectedSni_AmongOtherCodes()
    {
        // The `sni_codes && @protected_sni` overlap protects a multi-SNI row if ANY of its codes is the
        // protected one — the row could be part of the over-cap tail counted under that 5-digit code.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);
        var store = new ScbCompanyRegisterStore(ctx.Db);

        await store.UpsertBatchAsync([Entry("5560000201", sni: ["46900", "70100", "62010"])], T0, ct);

        var deregistered = await store.DeregisterMissingAsync(
            T1, [new ScbProtectedPartition("0180", "70100")], ct);

        deregistered.ShouldBe(0);   // the row carries 70100 → shielded
        (await ReadAllAsync(ctx.Db, ct)).Single().Status.ShouldBe(CompanyRegisterStatus.Active);
    }

    [Fact]
    public async Task UpsertBatch_ResurrectsDeregisteredRow_WhenItReappears()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);
        var store = new ScbCompanyRegisterStore(ctx.Db);

        await store.UpsertBatchAsync([Entry("5560000045")], T0, ct);
        await store.DeregisterMissingAsync(T1, [], ct);   // …45 (synced_at T0 < T1) → Deregistered
        (await ReadAllAsync(ctx.Db, ct)).Single().Status.ShouldBe(CompanyRegisterStatus.Deregistered);

        // It reappears in a later extract as Active → resurrected.
        await store.UpsertBatchAsync([Entry("5560000045", status: CompanyRegisterStatus.Active)], T2, ct);

        var row = (await ReadAllAsync(ctx.Db, ct)).Single();
        row.Status.ShouldBe(CompanyRegisterStatus.Active);
        row.SyncedAt.ShouldBe(T2);
    }

    [Fact]
    public async Task UpsertBatch_RoundTripsSniTextArray()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);
        var store = new ScbCompanyRegisterStore(ctx.Db);

        await store.UpsertBatchAsync([Entry("5560000056", sni: ["29100", "45200", "46900"])], T0, ct);

        var row = (await ReadAllAsync(ctx.Db, ct)).Single();
        row.SniCodes.ShouldBe(["29100", "45200", "46900"]);
    }

    [Fact]
    public async Task GetMaxObservedTotalRowsFetched_IsNull_WhenNoPriorRun()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);
        var store = new ScbCompanyRegisterStore(ctx.Db);

        (await store.GetMaxObservedTotalRowsFetchedAsync(days: 90, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task GetMaxObservedTotalRowsFetched_IsNull_AfterFreshContext_EvenWhenAPriorSyncAuditRowExists()
    {
        // #685 (order-independent regression pin): a sibling in the shared "Worker" collection can leave a
        // recent System.CompanyRegisterSynced audit row (TotalRowsFetched=1000). FreshContextAsync must
        // clear those rows too, not just company_register, so "no prior run" holds regardless of order.
        // Seed the contaminating row via the REAL write path, then take a fresh context and assert null —
        // this FAILS without the FreshContextAsync audit-clear (returns 1000) and PASSES with it.
        var ct = TestContext.Current.CancellationToken;
        await using (var seed = await FreshContextAsync(ct))
        {
            var auditor = seed.Scope.ServiceProvider.GetRequiredService<ISystemEventAuditor>();
            await auditor.RecordAsync(
                new CompanyRegisterSynced(
                    AggregateId: Guid.NewGuid(),
                    OccurredAt: T1,                 // inside the 90-day window relative to the real clock
                    RowsUpserted: 1000,
                    RowsDeregistered: 0,
                    RowsExcludedPersonnummerShaped: 0,
                    RowsExcludedInvalid: 0,
                    TotalRowsFetched: 1000,
                    SweepApplied: true,
                    SweepSkipReason: null,
                    ProtectedPartitionCount: 0,
                    FailedPartitionCount: 0,
                    StartedAt: T0,
                    CompletedAt: T1),
                ct);
        }

        await using var ctx = await FreshContextAsync(ct);
        var store = new ScbCompanyRegisterStore(ctx.Db);

        (await store.GetMaxObservedTotalRowsFetchedAsync(days: 90, ct)).ShouldBeNull();
    }

    private async Task<ScopedContext> FreshContextAsync(CancellationToken ct)
    {
        var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE company_register;", ct);
        // #685 — also clear the CompanyRegisterSynced run-watermark audit rows the floor-baseline read
        // scans (GetMaxObservedTotalRowsFetchedAsync). Without this the test only owns company_register,
        // so a sibling's audit row (TotalRowsFetched=1000) leaks into the "no prior run" assertion under
        // certain suite orders (audit_log is day-partitioned; a scoped DELETE routes across partitions).
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM audit_log WHERE event_type = 'System.CompanyRegisterSynced';", ct);
        return new ScopedContext(scope, db);
    }

    private static Task<List<ScbCompanyRegisterEntry>> ReadAllAsync(AppDbContext db, CancellationToken ct) =>
        db.Set<ScbCompanyRegisterEntry>().AsNoTracking()
            .OrderBy(e => e.OrganizationNumber).ToListAsync(ct);

    private sealed class ScopedContext(AsyncServiceScope scope, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public AsyncServiceScope Scope { get; } = scope;
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }
}

/// <summary>
/// #688 (ADR 0091 amendment 2026-07-05) — value-pin for the two per-command timeout constants on
/// <see cref="ScbCompanyRegisterStore"/>. Raw <c>NpgsqlCommand</c>s inherit the 30 s connection-string
/// default; these explicit timeouts (120 s population / 600 s full-table sweep) are a deliberate speed
/// bump — changing the numbers must be a conscious edit. A <c>const</c> read needs no DB, so this is a
/// PLAIN class (deliberately NOT in the Testcontainers <c>[Collection("Worker")]</c>): it runs without
/// Docker. The behavioral proof that the commands still execute WITH these timeouts applied lives in the
/// Testcontainers <see cref="ScbCompanyRegisterStoreTests"/> (upsert + sweep). Per the CTO bind (Q6),
/// a live-command <c>CommandTimeout</c> reflection assertion is deliberately NOT attempted (low ROI —
/// the command is internal and disposed).
/// </summary>
public class ScbCompanyRegisterStoreTimeoutConstantsTests
{
    [Fact]
    public void ScbCompanyRegisterStore_CommandTimeoutSeconds_Is120()
    {
        // Applied to the jsonb batch upsert and the baseline-read scalar — ~4x the 30 s that failed under
        // contention on the first live run, still bounded so a genuinely hung command fails loud.
        ScbCompanyRegisterStore.CommandTimeoutSeconds.ShouldBe(120);
    }

    [Fact]
    public void ScbCompanyRegisterStore_SweepCommandTimeoutSeconds_Is600()
    {
        // Applied to the full-table single-column deregister UPDATE over ~1.17M rows; anchored to the
        // in-repo MigrationsOptionsFactory 600 s bulk-statement precedent, not an invented value.
        ScbCompanyRegisterStore.SweepCommandTimeoutSeconds.ShouldBe(600);
    }
}
