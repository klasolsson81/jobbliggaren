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
/// and the deregister sweep are Postgres-specific — never EF-InMemory). Each test TRUNCATEs
/// <c>company_register</c> first: the deregister sweep is a whole-table operation, so tests must own the
/// table (the "Worker" collection runs serially and no other test touches this table).
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
        params string[] sni) =>
        new()
        {
            OrganizationNumber = orgNr,
            Name = name,
            SeatMunicipalityCode = "0180",
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

        var deregistered = await store.DeregisterMissingAsync(T1, ct);

        deregistered.ShouldBe(1);
        var rows = await ReadAllAsync(ctx.Db, ct);
        rows.Count.ShouldBe(2);                                              // nothing hard-deleted
        rows.Single(r => r.OrganizationNumber == "5560000023").Status.ShouldBe(CompanyRegisterStatus.Active);
        rows.Single(r => r.OrganizationNumber == "5560000034").Status.ShouldBe(CompanyRegisterStatus.Deregistered);
    }

    [Fact]
    public async Task UpsertBatch_ResurrectsDeregisteredRow_WhenItReappears()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await FreshContextAsync(ct);
        var store = new ScbCompanyRegisterStore(ctx.Db);

        await store.UpsertBatchAsync([Entry("5560000045")], T0, ct);
        await store.DeregisterMissingAsync(T1, ct);   // …45 (synced_at T0 < T1) → Deregistered
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

    private async Task<ScopedContext> FreshContextAsync(CancellationToken ct)
    {
        var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE company_register;", ct);
        return new ScopedContext(scope, db);
    }

    private static Task<List<ScbCompanyRegisterEntry>> ReadAllAsync(AppDbContext db, CancellationToken ct) =>
        db.Set<ScbCompanyRegisterEntry>().AsNoTracking()
            .OrderBy(e => e.OrganizationNumber).ToListAsync(ct);

    private sealed class ScopedContext(AsyncServiceScope scope, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
