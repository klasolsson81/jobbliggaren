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
//   5. F4a BACK-COMPAT (binding, BC-4): a row written BEFORE the Regions axis existed has NO "Regions"
//      key in its jsonb. It must read back as Regions == [] (a kommun-only filter, which is exactly
//      what it meant) with Municipalities/OnlyMatched intact — never a crash, never a lost axis. The
//      forward direction is pinned too: the writer ALWAYS emits an explicit Regions array (canonical
//      form), so a spec with no regions stores "Regions": [] rather than omitting the key.
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
        var filter = WatchFilterSpec.Create(["gbg_kn", "sthlm_kn"], [], onlyMatched: true).Value;
        var watchId = await SeedWatchAsync(filter, ct);

        // Reload in a FRESH scope/context — proves the value came off disk, not the change tracker.
        var reloaded = await ReloadWatchAsync(watchId, ct);

        reloaded.Filter.ShouldNotBeNull();
        reloaded.Filter!.Municipalities.ShouldBe(["gbg_kn", "sthlm_kn"]); // normaliserad ordinal
        reloaded.Filter.OnlyMatched.ShouldBeTrue();
        reloaded.Filter.ShouldBe(filter); // strukturell equality (Equals) bevarad över jsonb-round-trip
    }

    // ── (1b) F4a — BOTH geo axes round-trip; the writer always emits an explicit Regions array ───

    [Fact]
    public async Task WatchWithRegions_RoundTripsThroughEf_StructurallyEqual()
    {
        var ct = TestContext.Current.CancellationToken;
        var filter = WatchFilterSpec.Create(["gbg_kn"], ["skane_lan", "vgot_lan"], onlyMatched: false).Value;
        var watchId = await SeedWatchAsync(filter, ct);

        var reloaded = await ReloadWatchAsync(watchId, ct);

        reloaded.Filter.ShouldNotBeNull();
        reloaded.Filter!.Regions.ShouldBe(["skane_lan", "vgot_lan"],
            "län-axeln måste överleva jsonb-round-trippen — annars tappas hela län-valet tyst vid omläsning");
        reloaded.Filter.Municipalities.ShouldBe(["gbg_kn"]);
        reloaded.Filter.ShouldBe(filter);

        // jsonb-nyckelkontraktet är PascalCase = VO:ns propertynamn.
        var raw = await ReadRawFilterAsync(watchId, ct);
        raw.ShouldNotBeNull();
        raw!.ShouldContain("\"Regions\"");
    }

    [Fact]
    public async Task WatchWithoutRegions_WritesExplicitEmptyRegionsArray_CanonicalForm()
    {
        // The writer emits BOTH axes unconditionally, so the on-disk form is deterministic (a key that
        // is sometimes absent and sometimes empty makes jsonb equality/diffing a coin-flip).
        var ct = TestContext.Current.CancellationToken;
        var filter = WatchFilterSpec.Create(["gbg_kn"], [], onlyMatched: false).Value;
        var watchId = await SeedWatchAsync(filter, ct);

        var raw = await ReadRawFilterAsync(watchId, ct);

        // En kommun-only-spec ska lagra en EXPLICIT tom Regions-array, inte utelämna nyckeln.
        raw.ShouldNotBeNull();
        raw!.Replace(" ", string.Empty, StringComparison.Ordinal)
            .ShouldContain("\"Regions\":[]");
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

    // ── (5) F4a BACK-COMPAT (binding, BC-4) — a pre-Regions jsonb row reads as an empty region axis ─

    [Fact]
    public async Task LegacyRow_WithoutRegionsKey_ReadsAsEmptyRegions()
    {
        // A row written BEFORE F4a has NO "Regions" key at all. It must deserialize into a kommun-only
        // spec (Regions == []), which is precisely what it meant — the converter re-validates through
        // WatchFilterSpec.Create, so a mishandled missing key would surface either as a crash
        // ("Lagrad WatchFilterSpec-jsonb bröt domän-invariant") or, worse, as a silently dropped
        // municipality axis. Raw SQL bypasses the converter, so this row is genuinely legacy-shaped.
        var ct = TestContext.Current.CancellationToken;
        var watchId = await InsertRawWatchWithLegacyFilterAsync(
            """{"Municipalities": ["gbg_kn", "sthlm_kn"], "OnlyMatched": true}""", ct);

        var reloaded = await ReloadWatchAsync(watchId, ct);

        reloaded.Filter.ShouldNotBeNull(
            "en pre-F4a-rad måste kunna läsas — aldrig krascha på den saknade Regions-nyckeln");
        reloaded.Filter!.Regions.ShouldBeEmpty(
            "en rad utan Regions-nyckel betyder tom län-axel (kommun-only-filter)");
        reloaded.Filter.Municipalities.ShouldBe(["gbg_kn", "sthlm_kn"],
            "kommun-axeln och OnlyMatched måste överleva orörda");
        reloaded.Filter.OnlyMatched.ShouldBeTrue();
    }

    [Fact]
    public async Task LegacyRow_OnlyMatchedWithoutGeoKeys_ReadsAsEmptyBothAxes()
    {
        // The other pre-F4a shape: an "endast matchade"-only filter. Both geo axes read as empty — and
        // the empty-spec invariant must NOT fire (OnlyMatched alone is a legal spec).
        var ct = TestContext.Current.CancellationToken;
        var watchId = await InsertRawWatchWithLegacyFilterAsync(
            """{"Municipalities": [], "OnlyMatched": true}""", ct);

        var reloaded = await ReloadWatchAsync(watchId, ct);

        reloaded.Filter.ShouldNotBeNull();
        reloaded.Filter!.Municipalities.ShouldBeEmpty();
        reloaded.Filter.Regions.ShouldBeEmpty();
        reloaded.Filter.OnlyMatched.ShouldBeTrue();
    }

    // ── (6) #551 PR-B D6 — the remote/distans axis round-trips + legacy back-compat ──────────────

    [Fact]
    public async Task WatchWithRemote_RoundTripsThroughEf_StructurallyEqual()
    {
        var ct = TestContext.Current.CancellationToken;
        // A remote-ONLY spec (no ort, not OnlyMatched) is valid and must survive the jsonb round-trip.
        var filter = WatchFilterSpec.Create(
            municipalities: null, regions: null, onlyMatched: false, remote: true).Value;
        var watchId = await SeedWatchAsync(filter, ct);

        var reloaded = await ReloadWatchAsync(watchId, ct);

        reloaded.Filter.ShouldNotBeNull();
        reloaded.Filter!.Remote.ShouldBeTrue(
            "remote-axeln måste överleva jsonb-round-trippen — annars tappas distans-valet tyst");
        reloaded.Filter.ShouldBe(filter);

        var raw = await ReadRawFilterAsync(watchId, ct);
        raw.ShouldNotBeNull();
        raw!.Replace(" ", string.Empty, StringComparison.Ordinal).ShouldContain("\"Remote\":true");
    }

    [Fact]
    public async Task LegacyRow_WithoutRemoteKey_ReadsAsRemoteFalse()
    {
        // A row written BEFORE #551 has NO "Remote" key → Remote == false (no remote axis), the
        // back-compat direction. The other axes must survive untouched, never a crash on the missing key.
        var ct = TestContext.Current.CancellationToken;
        var watchId = await InsertRawWatchWithLegacyFilterAsync(
            """{"Municipalities": ["gbg_kn"], "Regions": [], "OnlyMatched": false}""", ct);

        var reloaded = await ReloadWatchAsync(watchId, ct);

        reloaded.Filter.ShouldNotBeNull(
            "en pre-#551-rad måste kunna läsas — aldrig krascha på den saknade Remote-nyckeln");
        reloaded.Filter!.Remote.ShouldBeFalse("en rad utan Remote-nyckel betyder ingen remote-axel");
        reloaded.Filter.Municipalities.ShouldBe(["gbg_kn"]);
    }

    // ── (4) Unfollow persistence (RF-2 sub-bind) — SoftDelete clears the column ON DISK ──────────

    [Fact]
    public async Task Unfollow_ClearsFilterColumn_ToNullOnDisk()
    {
        // SoftDelete (unfollow) nollställer Filter in-app; verifiera att rensningen NÅR kolumnen (filter
        // IS NULL on-disk), inte bara CLR-objektet. Raw-läsningen kringgår både konvertern och soft-
        // delete-query-filtren, så den läser den soft-deletade radens faktiska kolumnvärde.
        var ct = TestContext.Current.CancellationToken;
        var filter = WatchFilterSpec.Create(["sthlm_kn"], [], onlyMatched: true).Value;
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

    // Inserts a company_watches row via RAW SQL with a hand-written `filter` jsonb — the ONLY way to
    // produce a LEGACY-shaped payload (the EF writer always emits the current canonical form, so it
    // cannot express "a row written before the Regions key existed"). Bypasses the converter on write;
    // the read path under test is the converter.
    private async Task<CompanyWatchId> InsertRawWatchWithLegacyFilterAsync(
        string filterJson, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO company_watches
              (id, user_id, organization_number, target_type, created_at, filter)
            VALUES
              (@id, @uid, @orgnr, 'Employer', @now, @filter::jsonb)
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("uid", Guid.NewGuid());
        cmd.Parameters.AddWithValue("orgnr", NewOrgNr());
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("filter", filterJson);
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
