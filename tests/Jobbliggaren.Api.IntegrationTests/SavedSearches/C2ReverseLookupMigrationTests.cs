using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.SavedSearches;

// Fas C2 (Platsbanken sök-paritet, ADR 0067 Beslut 1/7; CTO-dom (c)/(d)/(f)
// 2026-06-09; architect F3). Verifierar C2-reverse-lookup-migrationens
// jsonb-transform mot riktig Postgres (Testcontainers) — InMemory fångar
// varken jsonb-operatorer, COLLATE "C"-sortering eller text[]-mappningen
// (jfr feedback_ef_strongly_typed_vo_contains_translation + B1/B2-mönstret
// i JobAdFacetsSurvivePurgeTests).
//
// Mekanik: fixture-DB:n är redan migrerad (ApiFactory kör MigrateAsync på
// färsk/tom DB). Transformen testas genom att (1) seeda legacy-jsonb-rader
// med rå SQL (kringgår EF-konvertern som fail-loud:ar på "Ssyk"), (2) köra
// EXAKT migrationens SQL via internal-ytan
// C2SearchParityReverseLookupAndRecentExpansion.BuildReverseLookupSql()
// (InternalsVisibleTo — ingen testkopia av SQL:en som kan glida), (3) läsa
// post-state både rått (jsonb-text) och genom SearchCriteriaJsonConverter.
//
// Kända mappnings-par ur den FRUSNA resursen (regenereras aldrig per CTO (c)
// → konstanterna är stabila för all framtid):
//   15e8_KDZ_31Z -> mcRJ_kq2_jFr
//   1Cjx_ooE_HT9 -> vPP6_rsw_dck
//   1DSQ_YqU_AMs -> vPP6_rsw_dck   (två yrken → samma grupp = distinct-case)
// Test-isolation (rename-collateral, ADR 0069). Tests in [Collection("Api")] share one
// Testcontainers Postgres. The C2 reverse-lookup migration replayed here scans AND mutates
// the WHOLE saved_searches table (guard RAISE EXCEPTIONs on any scalar/unmappable "Ssyk"
// across every row). A whole-table migration replay must own a clean precondition — it
// cannot rely on [Collection] execution order (name-based order is not a contract; the
// JobbPilot→Jobbliggaren rename reordered it and surfaced this latent shared-fixture
// coupling). #352: deriving from MalformedJsonbSeedTestBase clears saved_searches on BOTH
// entry and exit so the global scan sees only this test's own row. The per-test
// try/finally DeleteRowAsync calls stay — they guard mid-test isolation between the
// whole-table migration replays; the base only adds the class-exit safety net so a crash
// BEFORE a finally no longer leaks a toxic row to a neighbour.
[Collection("Api")]
public class C2ReverseLookupMigrationTests(ApiFactory factory)
    : MalformedJsonbSeedTestBase(factory)
{
    protected override IReadOnlyList<string> TablesToClear => ["saved_searches"];

    private const string MappedName1 = "15e8_KDZ_31Z";
    private const string MappedGroup1 = "mcRJ_kq2_jFr";
    private const string MappedName2 = "1Cjx_ooE_HT9";
    private const string MappedName3 = "1DSQ_YqU_AMs";
    private const string MappedGroup23 = "vPP6_rsw_dck";

    // ── (a) Mappad rad: transform + Ssyk-strip + converter-läsbar ───────────

    [Fact]
    public async Task ReverseLookup_ShouldTransformSsykToOccupationGroup_AndBeReadableThroughConverter()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await SeedLegacyRowAsync(db, id,
                $"{{\"Ssyk\":[\"{MappedName1}\"],\"Region\":[],\"Q\":null,\"SortBy\":0}}", ct);

            await RunReverseLookupSqlAsync(db, ct);

            // Rå post-state: Ssyk-nyckeln struken, OccupationGroup = mappat id,
            // övriga nycklar (Region/Q/SortBy) bevarade verbatim.
            using var doc = JsonDocument.Parse(await ReadCriteriaJsonAsync(db, id, ct));
            doc.RootElement.TryGetProperty("Ssyk", out _).ShouldBeFalse();
            doc.RootElement.GetProperty("OccupationGroup")
                .EnumerateArray().Select(e => e.GetString()).ShouldBe([MappedGroup1]);
            doc.RootElement.GetProperty("Region").GetArrayLength().ShouldBe(0);
            doc.RootElement.GetProperty("Q").ValueKind.ShouldBe(JsonValueKind.Null);
            doc.RootElement.GetProperty("SortBy").GetInt32().ShouldBe(0);

            // Läsbar genom SearchCriteriaJsonConverter (fail-loud:ar på "Ssyk"
            // — passerar bara om nyckeln verkligen är struken).
            var saved = await db.SavedSearches
                .SingleAsync(s => s.Id == new SavedSearchId(id), ct);
            saved.Criteria.OccupationGroup.ShouldBe([MappedGroup1]);
            saved.Criteria.Municipality.ShouldBeEmpty();
            saved.Criteria.Region.ShouldBeEmpty();
            saved.Criteria.Q.ShouldBeNull();

            // Idempotens (CTO (c).3): omkörning träffar 0 rader (nyckeln är
            // borta) → identiskt sluttillstånd, inget fel, ingen dubbelmappning.
            await RunReverseLookupSqlAsync(db, ct);
            using var doc2 = JsonDocument.Parse(await ReadCriteriaJsonAsync(db, id, ct));
            doc2.RootElement.GetProperty("OccupationGroup")
                .EnumerateArray().Select(e => e.GetString()).ShouldBe([MappedGroup1]);
            doc2.RootElement.TryGetProperty("Ssyk", out _).ShouldBeFalse();
        }
        finally
        {
            await DeleteRowAsync(db, id, ct);
        }
    }

    // ── (b) "Ssyk":[]-radklassen: nyckel-existens-predikatet (architect F3) ─

    [Fact]
    public async Task ReverseLookup_ShouldStripEmptySsykKey_AndWriteEmptyOccupationGroup()
    {
        // KÄRNAN: gamla Write emitterade ALLTID "Ssyk"-nyckeln — även [] på
        // Region/Q-only-rader. Predikatet är nyckel-EXISTENS (criteria ? 'Ssyk'),
        // inte icke-tom-array. Missas dessa rader kastar fail-loud-konvertern
        // på helt giltiga sökningar.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        const string region = "CifL_Rzy_Mku";

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await SeedLegacyRowAsync(db, id,
                $"{{\"Ssyk\":[],\"Region\":[\"{region}\"],\"Q\":null,\"SortBy\":0}}", ct);

            await RunReverseLookupSqlAsync(db, ct);

            using var doc = JsonDocument.Parse(await ReadCriteriaJsonAsync(db, id, ct));
            doc.RootElement.TryGetProperty("Ssyk", out _).ShouldBeFalse();
            doc.RootElement.GetProperty("OccupationGroup").GetArrayLength().ShouldBe(0);

            // Region-only-raden passerar tom-invarianten genom konvertern.
            var saved = await db.SavedSearches
                .SingleAsync(s => s.Id == new SavedSearchId(id), ct);
            saved.Criteria.OccupationGroup.ShouldBeEmpty();
            saved.Criteria.Region.ShouldBe([region]);
        }
        finally
        {
            await DeleteRowAsync(db, id, ct);
        }
    }

    // ── (c) Omappbart id: fail-loud ABORT (Saltzer/Schroeder) ───────────────

    [Fact]
    public async Task ReverseLookup_ShouldAbort_WhenRowCarriesUnmappableSsykId()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            // "5132" speglar dev-DB-fyndet (rå SSYK-kod, ej concept-id) —
            // exakt den omappbara klass som ALDRIG får droppas tyst.
            await SeedLegacyRowAsync(db, id,
                "{\"Ssyk\":[\"5132\"],\"Region\":[],\"Q\":null,\"SortBy\":0}", ct);

            var ex = await Should.ThrowAsync<PostgresException>(
                () => RunReverseLookupSqlAsync(db, ct));
            ex.MessageText.ShouldContain("omappbart Ssyk-id");

            // Raden är ORÖRD (abort före UPDATE — ingen partiell transform).
            using var doc = JsonDocument.Parse(await ReadCriteriaJsonAsync(db, id, ct));
            doc.RootElement.GetProperty("Ssyk")
                .EnumerateArray().Select(e => e.GetString()).ShouldBe(["5132"]);
            doc.RootElement.TryGetProperty("OccupationGroup", out _).ShouldBeFalse();
        }
        finally
        {
            await DeleteRowAsync(db, id, ct);
        }
    }

    // ── (c2) Skalär legacy-form: pedagogisk fail-loud (security Minor 2) ────

    [Fact]
    public async Task ReverseLookup_ShouldAbortWithClearMessage_WhenSsykIsScalarLegacyForm()
    {
        // Pre-F2-historisk form ("Ssyk":"id" — gamla konvertern tolererade
        // skalär on-read). typeof-checken i fail-loud-DO-blocket ger ett
        // pedagogiskt RAISE i stället för rått "cannot extract elements from
        // a scalar" (security-auditor C2 Minor 2 2026-06-10). Fail-safe-
        // egenskapen är densamma: abort, ingen tyst transform.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await SeedLegacyRowAsync(db, id,
                $"{{\"Ssyk\":\"{MappedName1}\",\"Region\":[],\"Q\":null,\"SortBy\":0}}", ct);

            var ex = await Should.ThrowAsync<PostgresException>(
                () => RunReverseLookupSqlAsync(db, ct));
            ex.MessageText.ShouldContain("icke-array-form");

            // Raden är ORÖRD (abort före UPDATE).
            using var doc = JsonDocument.Parse(await ReadCriteriaJsonAsync(db, id, ct));
            doc.RootElement.GetProperty("Ssyk").GetString().ShouldBe(MappedName1);
            doc.RootElement.TryGetProperty("OccupationGroup", out _).ShouldBeFalse();
        }
        finally
        {
            await DeleteRowAsync(db, id, ct);
        }
    }

    // ── (d) recent_job_searches: kolumnbyte applicerat ──────────────────────

    [Fact]
    public async Task RecentJobSearches_ShouldHaveNewNotNullColumns_AndSsykListGone()
    {
        // Migrationen applicerades av fixturen (MigrateAsync). NOT NULL utan
        // default kan bara adderas på tom tabell → att kolumnerna existerar
        // som NOT NULL bevisar samtidigt DELETE-före-DDL-ordningen i Up().
        var ct = TestContext.Current.CancellationToken;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var columns = await db.Database
            .SqlQueryRaw<ColumnRow>(
                """
                SELECT column_name, is_nullable, data_type
                FROM information_schema.columns
                WHERE table_name = 'recent_job_searches'
                """)
            .ToListAsync(ct);

        columns.ShouldContain(c =>
            c.ColumnName == "occupation_group_list" && c.IsNullable == "NO" && c.DataType == "ARRAY");
        columns.ShouldContain(c =>
            c.ColumnName == "municipality_list" && c.IsNullable == "NO" && c.DataType == "ARRAY");
        columns.ShouldNotContain(c => c.ColumnName == "ssyk_list");
    }

    // ── (e) Multi-element + dubbletter: sorterad distinct (COLLATE "C") ─────

    [Fact]
    public async Task ReverseLookup_ShouldProduceSortedDistinctOccupationGroups()
    {
        // Input i medvetet osorterad ordning + dubblett + två yrken som mappar
        // till SAMMA grupp. Förväntat lagrat: distinct + ordinal-sorterat
        // (COLLATE "C" = byte-ordning = StringComparer.Ordinal-paritet,
        // ADR 0042 Beslut B invariant 1 i LAGRAD form):
        //   [mcRJ_kq2_jFr, vPP6_rsw_dck]  ('m' 0x6D < 'v' 0x76)
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            await SeedLegacyRowAsync(db, id,
                $"{{\"Ssyk\":[\"{MappedName3}\",\"{MappedName2}\",\"{MappedName1}\",\"{MappedName2}\"],"
                + "\"Region\":[],\"Q\":null,\"SortBy\":0}", ct);

            await RunReverseLookupSqlAsync(db, ct);

            using var doc = JsonDocument.Parse(await ReadCriteriaJsonAsync(db, id, ct));
            doc.RootElement.GetProperty("OccupationGroup")
                .EnumerateArray().Select(e => e.GetString())
                .ShouldBe([MappedGroup1, MappedGroup23]);

            var saved = await db.SavedSearches
                .SingleAsync(s => s.Id == new SavedSearchId(id), ct);
            saved.Criteria.OccupationGroup.ShouldBe([MappedGroup1, MappedGroup23]);
        }
        finally
        {
            await DeleteRowAsync(db, id, ct);
        }
    }

    // ── Hjälpare ─────────────────────────────────────────────────────────────

    // Läs-DTO för information_schema (EF mappar property-namn → snake_case-
    // kolumner via modellens namnkonvention, jfr JobAdFacetsSurvivePurgeTests).
    private sealed record ColumnRow(string ColumnName, string IsNullable, string DataType);

    private static async Task SeedLegacyRowAsync(
        AppDbContext db, Guid id, string criteriaJson, CancellationToken ct)
    {
        // Rå SQL — EF-skrivvägen kan inte producera legacy-form ("Ssyk" skrivs
        // aldrig av nya konvertern) och fail-loud:ar dessutom vid läsning.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO saved_searches
                (id, job_seeker_id, name, notification_enabled, created_at, updated_at, criteria)
            VALUES ({0}, {1}, {2}, false, now(), now(), {3}::jsonb)
            """,
            [id, Guid.NewGuid(), $"C2-legacy {id:N}", criteriaJson],
            ct);
    }

    // Kör EXAKT migrationens statement-sekvens i EN session (temp-tabellen är
    // session-scoped → explicit OpenConnection håller samma fysiska anslutning
    // genom hela sekvensen). DROP IF EXISTS i finally städar efter fail-loud-
    // abort (Npgsql-pool-reset droppar annars temp-tabeller vid close, men
    // explicit städning är deterministisk).
    private static async Task RunReverseLookupSqlAsync(AppDbContext db, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            foreach (var sql in C2SearchParityReverseLookupAndRecentExpansion.BuildReverseLookupSql())
                await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS _occname_to_ssyk4;", ct);
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<string> ReadCriteriaJsonAsync(
        AppDbContext db, Guid id, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<string>(
                "SELECT criteria::text AS \"Value\" FROM saved_searches WHERE id = {0}", id)
            .ToListAsync(ct);
        return rows.ShouldHaveSingleItem();
    }

    private static async Task DeleteRowAsync(AppDbContext db, Guid id, CancellationToken ct)
        => await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM saved_searches WHERE id = {0}", [id], ct);
}
