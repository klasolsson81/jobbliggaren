using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

// Bevaknings-reconcile PR-F1 (issue #799, RF-2, 2026-07-12) — the WatchFilterSpecJsonConverter +
// nullable-jsonb mapping contract on the ACTUAL persistence path (real Postgres jsonb via
// AppDbContext; the converter is internal in Infrastructure and tested deliberately via the behaviour
// — the established pattern, see SearchCriteriaJsonbBackcompatTests / MatchPreferencesJsonbBackcompatTests).
// Testcontainers, NEVER EF-InMemory: only real Postgres round-trips the nullable jsonb `filter` column
// and honours SQL-NULL ↔ CLR-null (InMemory would hide the shadow-column/NULL semantics).
//
//   1. Roundtrip: a watch WITH a filter (municipalities + OnlyMatched) → reload in a FRESH context →
//      Filter is structurally equal (Equals) — the VO survives the jsonb serialize/deserialize.
//   2. A watch WITHOUT a filter → reload → Filter == null (SQL NULL = the canonical "show all" form;
//      EF never invokes the converter for a null CLR value).
//   3. BACK-COMPAT (binding): a raw-SQL company_watches row with the `filter` column left NULL (a
//      pre-migration row — ADD COLUMN ... NULL, no backfill) → EF reads Filter == null, never a crash.
//   4. Unfollow persistence (RF-2 sub-bind): a watch with a filter → SoftDelete → SaveChanges → a raw
//      SQL read shows filter IS NULL on-disk (the in-app clear REACHES the column — no latent
//      profiling-adjacent data on the soft-deleted row, Art. 5(1)(c)/(e)).
//
// Test-isolation (#352): this fixture seeds raw/aggregate rows into the VO-bearing company_watches
// table shared by [Collection("Api")]. Deriving from MalformedJsonbSeedTestBase clears company_watches
// on BOTH entry and exit so neither this fixture nor its CompanyWatch neighbours depend on execution
// order. Raw SQL bypasses the converter — a would-be toxic row can only be deleted, never read back.
[Collection("Api")]
public sealed class CompanyWatchFilterJsonbBackcompatTests(ApiFactory factory)
    : MalformedJsonbSeedTestBase(factory)
{
    protected override IReadOnlyList<string> TablesToClear => ["company_watches"];

    // ── (1) Roundtrip — Write → Read (fresh context) = structurally equal VO ─────────────────────

    [Fact]
    public async Task WatchWithFilter_RoundTripsThroughEf_StructurallyEqual()
    {
        var ct = TestContext.Current.CancellationToken;
        var filter = WatchFilterSpec.Create(["gbg_kn", "sthlm_kn"], onlyMatched: true).Value;
        var watchId = await SeedWatchAsync(filter, ct);

        // Reload in a FRESH scope/context — proves the value came off disk, not the change tracker.
        var reloaded = await ReloadWatchAsync(watchId, ct);

        reloaded.Filter.ShouldNotBeNull();
        reloaded.Filter!.Municipalities.ShouldBe(["gbg_kn", "sthlm_kn"]); // normaliserad ordinal
        reloaded.Filter.OnlyMatched.ShouldBeTrue();
        reloaded.Filter.ShouldBe(filter); // strukturell equality (Equals) bevarad över jsonb-round-trip
    }

    // ── (2) No filter → SQL NULL → CLR null ──────────────────────────────────────────────────────

    [Fact]
    public async Task WatchWithoutFilter_RoundTrips_AsNullFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var watchId = await SeedWatchAsync(filter: null, ct);

        var reloaded = await ReloadWatchAsync(watchId, ct);

        reloaded.Filter.ShouldBeNull(
            "en watch utan filter persisteras som SQL NULL (den kanoniska show-all-formen)");
    }

    // ── (3) BACK-COMPAT (binding) — a pre-migration row with a NULL filter column reads as null ──

    [Fact]
    public async Task LegacyRow_WithNullFilterColumn_ReadsAsNullFilter()
    {
        // A row stored BEFORE the filter column existed (ADD COLUMN ... NULL, no backfill) → EF reads
        // Filter == null (it never invokes the converter for NULL). The raw INSERT omits the `filter`
        // column entirely, leaving it NULL exactly like a pre-migration row — the binding claim that
        // every existing company_watches row is back-compatible for free.
        var ct = TestContext.Current.CancellationToken;
        var watchId = await InsertRawWatchWithoutFilterAsync(ct);

        var reloaded = await ReloadWatchAsync(watchId, ct);

        reloaded.Filter.ShouldBeNull(
            "en pre-migration-rad (filter-kolumnen NULL) måste läsas som Filter == null, aldrig krascha");
    }

    // ── (4) Unfollow persistence (RF-2 sub-bind) — SoftDelete clears the column ON DISK ──────────

    [Fact]
    public async Task Unfollow_ClearsFilterColumn_ToNullOnDisk()
    {
        // SoftDelete (unfollow) nollställer Filter in-app; verifiera att rensningen NÅR kolumnen (filter
        // IS NULL on-disk), inte bara CLR-objektet. Raw-läsningen kringgår både konvertern och soft-
        // delete-query-filtren, så den läser den soft-deletade radens faktiska kolumnvärde.
        var ct = TestContext.Current.CancellationToken;
        var filter = WatchFilterSpec.Create(["sthlm_kn"], onlyMatched: true).Value;
        var watchId = await SeedWatchAsync(filter, ct);

        await UnfollowAsync(watchId, ct);

        var rawFilter = await ReadRawFilterAsync(watchId, ct);
        rawFilter.ShouldBeNull("SoftDelete måste nollställa filter-kolumnen på disk (RF-2 sub-bind)");
    }

    // ─────────────────────────── Seeding / reload helpers

    // Seeds a follow via the aggregate (EF fills all mapped columns) with an OPTIONAL per-watch filter.
    // A fresh userId + org.nr per call keeps the active-partial UNIQUE(user_id, org.nr) collision-free.
    private async Task<CompanyWatchId> SeedWatchAsync(WatchFilterSpec? filter, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var watch = CompanyWatch.Follow(
            Guid.NewGuid(), OrganizationNumber.Create(NewOrgNr()).Value, clock).Value;
        if (filter is not null)
            watch.SetFilter(filter).IsSuccess.ShouldBeTrue("SetFilter ska lyckas på en aktiv watch");
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    // Loads the watch in a FRESH scope/context (proves the value round-tripped through the DB, not the
    // change tracker). The active watch passes the soft-delete query filter.
    private async Task<CompanyWatch> ReloadWatchAsync(CompanyWatchId watchId, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CompanyWatches.AsNoTracking().SingleAsync(w => w.Id == watchId, ct);
    }

    // Loads the watch TRACKED, unfollows (SoftDelete → clears Filter in-app), and persists.
    private async Task UnfollowAsync(CompanyWatchId watchId, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var watch = await db.CompanyWatches.SingleAsync(w => w.Id == watchId, ct);
        watch.SoftDelete(clock);
        await db.SaveChangesAsync(ct);
    }

    // Inserts a company_watches row via RAW SQL, OMITTING the `filter` column → it defaults to NULL,
    // simulating a pre-migration row (bypasses the EF converter entirely). target_type is stored by
    // enum NAME ("Employer").
    private async Task<CompanyWatchId> InsertRawWatchWithoutFilterAsync(CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO company_watches
              (id, user_id, organization_number, target_type, created_at)
            VALUES
              (@id, @uid, @orgnr, 'Employer', @now)
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("uid", Guid.NewGuid());
        cmd.Parameters.AddWithValue("orgnr", NewOrgNr());
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync(ct);
        await conn.CloseAsync();
        return new CompanyWatchId(id);
    }

    // Reads the filter column as RAW text (verifies the on-disk form, bypassing the converter AND the
    // soft-delete query filter). Returns null when the column IS NULL.
    private async Task<string?> ReadRawFilterAsync(CompanyWatchId watchId, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT filter::text FROM company_watches WHERE id = @id";
        cmd.Parameters.AddWithValue("id", watchId.Value);
        var raw = await cmd.ExecuteScalarAsync(ct);
        await conn.CloseAsync();
        return raw is null or DBNull ? null : (string)raw;
    }

    // 10-digit legal-entity org.nr (third digit ≥ 2), unique per call — OrganizationNumber.Create
    // validates 10 digits (no Luhn).
    private static string NewOrgNr() =>
        $"55{Random.Shared.Next(10000000, 99999999)}";
}
