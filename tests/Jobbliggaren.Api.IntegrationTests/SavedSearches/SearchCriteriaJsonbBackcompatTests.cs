using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.SavedSearches;

// C2 (ADR 0067, CTO-dom (f) + architect F2) — SearchCriteriaJsonConverter-
// kontraktet på den faktiska persistensvägen (riktig Postgres jsonb via
// AppDbContext; konverter-klassen är internal i Infrastructure och testas
// medvetet via beteendet — etablerat mönster sedan Batch 3):
//
//   1. Read: nya nycklar "OccupationGroup"/"Municipality" (sträng-eller-
//      array-tolerans, default-deny för övriga former).
//   2. Read: legacy-nyckeln "Ssyk" → FAIL-LOUD JsonException (även "Ssyk":[]).
//      ALDRIG tyst Skip() — tyst droppning kunde amputera en bevaknings
//      yrke-dimension (Saltzer/Schroeder fail-safe default).
//   3. Read: saknade nya nycklar i gammal rad → tomma listor → Create
//      passerar (bakåtkompat-invariant 4).
//   4. Write: ordning OccupationGroup, Municipality, Region, Q, SortBy —
//      skriver ALDRIG "Ssyk" (skrivvägen kan per konstruktion inte
//      producera en rad som triggar fail-loud-casen).
//   5. Roundtrip: Write → Read = strukturellt samma VO.
//
// RÖD tills konvertern implementerar C2-formen.
// Test-isolation (rename-collateral, ADR 0069). This fixture inserts legacy "Ssyk" rows
// into the shared [Collection("Api")] saved_searches table; left behind they poison any
// whole-table assertion in the collection (e.g. C2ReverseLookupMigrationTests' migration
// guard). #352: the class now derives from MalformedJsonbSeedTestBase, which clears
// saved_searches on BOTH entry and exit — so neither this fixture nor its neighbours depend
// on [Collection] execution order (the JobbPilot→Jobbliggaren rename reordered it), and the
// C2-neighbour hazard noted above is closed even when this fixture's last toxic-seed test has
// no successor to DELETE its row.
[Collection("Api")]
public sealed class SearchCriteriaJsonbBackcompatTests(ApiFactory factory)
    : MalformedJsonbSeedTestBase(factory)
{
    protected override IReadOnlyList<string> TablesToClear => ["saved_searches"];

    private static async Task<JobSeeker> SeedSeekerAsync(AppDbContext db, IDateTimeProvider clock, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Backcompat User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    // Skriver en saved_searches-rad direkt med RÅ jsonb (kringgår EF-mappningen)
    // för att simulera en redan-persistent rad i godtycklig form.
    private async Task<Guid> InsertRawSavedSearchAsync(
        Guid jobSeekerId, string criteriaJson, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO saved_searches
              (id, job_seeker_id, name, criteria, notification_enabled,
               created_at, updated_at)
            VALUES
              (@id, @sid, @name, @criteria::jsonb, false, @now, @now)
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sid", jobSeekerId);
        cmd.Parameters.AddWithValue("name", "Rå jsonb-rad");
        cmd.Parameters.AddWithValue("criteria", criteriaJson);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync(ct);
        await conn.CloseAsync();
        return id;
    }

    // Läser kolumnen criteria som RÅ text (verifierar Write-formen on-disk,
    // inte bara vad konvertern läser tillbaka).
    private async Task<string> ReadRawCriteriaAsync(Guid savedSearchId, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT criteria::text FROM saved_searches WHERE id = @id";
        cmd.Parameters.AddWithValue("id", savedSearchId);
        var raw = (string)(await cmd.ExecuteScalarAsync(ct))!;
        await conn.CloseAsync();
        return raw;
    }

    private static string FlattenMessages(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            messages.Add(e.Message);
        return string.Join(" | ", messages);
    }

    // ---------------------------------------------------------------
    // (1) Nya nycklar — sträng-eller-array-tolerans
    // ---------------------------------------------------------------

    [Fact]
    public async Task NewKeys_ScalarForm_ReadsAsSingleElementList()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await SeedSeekerAsync(db, clock, ct);

        // Skalär-tolerans per ReadStringOrStringArray-återbruket (architect F2).
        var json = """{"OccupationGroup":"g1","Municipality":"m1","Region":"y","Q":null,"SortBy":0}""";
        var id = await InsertRawSavedSearchAsync(seeker.Id.Value, json, ct);

        using var readScope = Factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await readDb.SavedSearches
            .SingleAsync(s => s.Id == new SavedSearchId(id), ct);

        saved.Criteria.OccupationGroup.ShouldBe(["g1"]);
        saved.Criteria.Municipality.ShouldBe(["m1"]);
        saved.Criteria.Region.ShouldBe(["y"]);
        saved.Criteria.Q.ShouldBeNull();
        saved.Criteria.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
    }

    [Fact]
    public async Task NewKeys_ArrayForm_ReadsAsList()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await SeedSeekerAsync(db, clock, ct);

        var json = """{"OccupationGroup":["g1","g2"],"Municipality":["m1"],"Region":[],"Q":"backend","SortBy":0}""";
        var id = await InsertRawSavedSearchAsync(seeker.Id.Value, json, ct);

        using var readScope = Factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await readDb.SavedSearches
            .SingleAsync(s => s.Id == new SavedSearchId(id), ct);

        saved.Criteria.OccupationGroup.ShouldBe(["g1", "g2"]);
        saved.Criteria.Municipality.ShouldBe(["m1"]);
        saved.Criteria.Region.ShouldBeEmpty();
        saved.Criteria.Q.ShouldBe("backend");
    }

    // ---------------------------------------------------------------
    // (2) Legacy-"Ssyk"-nyckel — FAIL-LOUD, aldrig tyst Skip()
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("""{"Ssyk":"x","Region":"y","Q":null,"SortBy":0}""")]            // gammal skalär
    [InlineData("""{"Ssyk":["x"],"Region":["y"],"Q":null,"SortBy":0}""")]        // gammal array
    [InlineData("""{"Ssyk":[],"Region":["y"],"Q":null,"SortBy":0}""")]           // TOM array — Write skrev ALLTID nyckeln
    public async Task LegacySsykKey_FailsLoud_WithMigrationGuidance(string legacyJson)
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await SeedSeekerAsync(db, clock, ct);
        var id = await InsertRawSavedSearchAsync(seeker.Id.Value, legacyJson, ct);

        using var readScope = Factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Should.ThrowAsync<Exception>(async () =>
            await readDb.SavedSearches.SingleAsync(s => s.Id == new SavedSearchId(id), ct));

        // Feltexten ska peka mot rotorsak + åtgärd (CTO-dom (f): "applicera
        // migrationen i stället för att tyst droppa yrke-dimensionen").
        var messages = FlattenMessages(ex);
        messages.ShouldContain("legacy");
        messages.ShouldContain("OccupationGroup");
        messages.ShouldContain("migrationen");
    }

    // ---------------------------------------------------------------
    // (3) Saknade nya nycklar — tomma listor → Create passerar
    // ---------------------------------------------------------------

    [Fact]
    public async Task MissingNewKeys_ReadAsEmptyLists_CreatePasses()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await SeedSeekerAsync(db, clock, ct);

        // Post-migration Region/Q-only-rad: varken OccupationGroup eller
        // Municipality-nyckel finns → tomma listor (bakåtkompat-invariant 4).
        var json = """{"Region":["y"],"Q":null,"SortBy":0}""";
        var id = await InsertRawSavedSearchAsync(seeker.Id.Value, json, ct);

        using var readScope = Factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await readDb.SavedSearches
            .SingleAsync(s => s.Id == new SavedSearchId(id), ct);

        saved.Criteria.OccupationGroup.ShouldBeEmpty();
        saved.Criteria.Municipality.ShouldBeEmpty();
        saved.Criteria.Region.ShouldBe(["y"]);
    }

    [Fact]
    public async Task MissingNewDimensionKeys_ReadAsEmptyLists_CreatePasses()
    {
        // B2 (ADR 0067 Beslut 6/7) bakåtkompat: en rad lagrad FÖRE
        // EmploymentType/WorktimeExtent infördes (varken nyckel finns) ska läsas
        // som tomma listor → Create passerar (saknad nyckel → tom lista, samma
        // tolerans-mönster som OccupationGroup/Municipality i (3)).
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await SeedSeekerAsync(db, clock, ct);

        // Pre-B2-rad: nya dimensions-nycklar saknas helt; minst ett annat
        // kriterium (OccupationGroup) håller raden giltig.
        var json = """{"OccupationGroup":["g1"],"Municipality":[],"Region":[],"Q":null,"SortBy":0}""";
        var id = await InsertRawSavedSearchAsync(seeker.Id.Value, json, ct);

        using var readScope = Factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await readDb.SavedSearches
            .SingleAsync(s => s.Id == new SavedSearchId(id), ct);

        saved.Criteria.EmploymentType.ShouldBeEmpty();
        saved.Criteria.WorktimeExtent.ShouldBeEmpty();
        saved.Criteria.OccupationGroup.ShouldBe(["g1"]);
    }

    [Fact]
    public async Task MissingEmployerKey_ReadsAsEmptyList_CreatePasses()
    {
        // #311 PR-2b C1 (ADR 0087 D6) bakåtkompat (CTO-bind D4, obligatoriskt): en SavedSearch-rad
        // lagrad FÖRE Employer-nyckeln infördes (nyckeln saknas helt) ska läsas som en tom lista →
        // Create passerar (saknad nyckel → switch-default → [], samma tolerans-mönster som Klass 2).
        // ALDRIG fail-loud (det är reserverat för den BORTTAGNA "Ssyk"-nyckeln; en TILLAGD nyckels
        // frånvaro är benign).
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await SeedSeekerAsync(db, clock, ct);

        // Pre-C1-rad: Employer-nyckeln saknas helt (även EmploymentType/WorktimeExtent finns här,
        // så raden är i övrigt en aktuell form) — bara den nya arbetsgivar-nyckeln är frånvarande.
        var json = """{"OccupationGroup":["g1"],"Municipality":[],"Region":[],"EmploymentType":[],"WorktimeExtent":[],"Q":null,"SortBy":0}""";
        var id = await InsertRawSavedSearchAsync(seeker.Id.Value, json, ct);

        using var readScope = Factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await readDb.SavedSearches
            .SingleAsync(s => s.Id == new SavedSearchId(id), ct);

        saved.Criteria.Employer.ShouldBeEmpty();
        saved.Criteria.OccupationGroup.ShouldBe(["g1"]);
    }

    // ---------------------------------------------------------------
    // (4) Write-form on-disk — nya nycklar skrivs, "Ssyk" skrivs ALDRIG.
    // OBS: jsonb normaliserar nyckelordning i lagrad form — Write-blockets
    // ORDNING (OccupationGroup, Municipality, Region, Q, SortBy per architect
    // F2) är därför inte observerbar här; den bär ingen semantik post-lagring
    // (canonical-hash-ordningen ägs av FilterHashCalculator och låses i
    // FilterHashCalculatorTests). Här låses nyckel-NÄRVARON.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Write_EmitsNewKeys_AndNeverSsyk()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await SeedSeekerAsync(db, clock, ct);

        var criteria = SearchCriteria.Create(
            occupationGroup: ["g1"], municipality: ["m1"], region: ["r1"],
            employmentType: ["et_fast"], worktimeExtent: ["wt_heltid"],
            employer: ["5566010101"],
            q: "backend", sortBy: JobAdSortBy.PublishedAtDesc).Value;
        var saved = SavedSearch.Create(seeker.Id, "Write-form", criteria, false, clock).Value;
        db.SavedSearches.Add(saved);
        await db.SaveChangesAsync(ct);

        var raw = await ReadRawCriteriaAsync(saved.Id.Value, ct);

        // Skrivvägen producerar per konstruktion aldrig en fail-loud-rad.
        raw.ShouldNotContain("\"Ssyk\"");
        raw.ShouldContain("\"OccupationGroup\"");
        raw.ShouldContain("\"Municipality\"");
        raw.ShouldContain("\"Region\"");
        // B2 (ADR 0067 Beslut 6/7): de två nya dimensionerna skrivs alltid
        // (array-form, mellan Region och Q).
        raw.ShouldContain("\"EmploymentType\"");
        raw.ShouldContain("\"WorktimeExtent\"");
        // #311 PR-2b C1: Employer-nyckeln skrivs alltid (array-form, mellan WorktimeExtent och Q).
        raw.ShouldContain("\"Employer\"");
        raw.ShouldContain("\"Q\"");
        raw.ShouldContain("\"SortBy\"");
    }

    // ---------------------------------------------------------------
    // (5) Roundtrip — Write → Read = strukturellt samma VO
    // ---------------------------------------------------------------

    [Fact]
    public async Task NewForm_RoundTripsThroughEf()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await SeedSeekerAsync(db, clock, ct);

        var criteria = SearchCriteria.Create(
            occupationGroup: ["grpB", "grpA"],
            municipality: ["uppsala_kn", "sthlm_kn"],
            region: ["stockholm", "uppsala"],
            employmentType: ["et_vikariat", "et_fast"],
            worktimeExtent: ["wt_heltid"],
            employer: ["5566020202", "5566010101"],
            q: "backend",
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        var saved = SavedSearch.Create(seeker.Id, "Roundtrip-rad", criteria, false, clock).Value;
        db.SavedSearches.Add(saved);
        await db.SaveChangesAsync(ct);

        using var readScope = Factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await readDb.SavedSearches
            .SingleAsync(s => s.Id == saved.Id, ct);

        reloaded.Criteria.OccupationGroup.ShouldBe(["grpA", "grpB"]); // sorterad ordinal
        reloaded.Criteria.Municipality.ShouldBe(["sthlm_kn", "uppsala_kn"]);
        reloaded.Criteria.Region.ShouldBe(["stockholm", "uppsala"]);
        reloaded.Criteria.EmploymentType.ShouldBe(["et_fast", "et_vikariat"]); // sorterad ordinal
        reloaded.Criteria.WorktimeExtent.ShouldBe(["wt_heltid"]);
        reloaded.Criteria.Employer.ShouldBe(["5566010101", "5566020202"]); // sorterad ordinal
        reloaded.Criteria.Q.ShouldBe("backend");
        reloaded.Criteria.ShouldBe(criteria); // strukturell equality bevarad
    }

    // ---------------------------------------------------------------
    // (6) Default-deny för nya nycklar — felaktiga former koerceras EJ tyst
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("""{"OccupationGroup":42,"Municipality":[],"Region":[],"Q":"xy","SortBy":0}""")]          // nummer
    [InlineData("""{"OccupationGroup":{"k":"v"},"Municipality":[],"Region":[],"Q":"xy","SortBy":0}""")]   // objekt
    [InlineData("""{"OccupationGroup":["ok",123],"Municipality":[],"Region":[],"Q":"xy","SortBy":0}""")]  // array m. icke-sträng
    [InlineData("""{"OccupationGroup":[],"Municipality":["ok",null],"Region":[],"Q":"xy","SortBy":0}""")] // null-element
    public async Task MalformedNewKeys_DefaultDeny_Throws(string malformedJson)
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await SeedSeekerAsync(db, clock, ct);
        var id = await InsertRawSavedSearchAsync(seeker.Id.Value, malformedJson, ct);

        using var readScope = Factory.Services.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Tolerant default-deny: tyst koercion förbjuden. Materialisering ska
        // kasta (JsonException/DomainException/InvalidOperationException-wrap).
        await Should.ThrowAsync<Exception>(async () =>
            await readDb.SavedSearches.SingleAsync(s => s.Id == new SavedSearchId(id), ct));
    }
}
