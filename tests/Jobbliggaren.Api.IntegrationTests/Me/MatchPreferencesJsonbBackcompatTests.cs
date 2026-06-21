using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.MyProfile;

// Spår 3 PR-A (ADR 0076-amendment) — MatchPreferencesJsonConverter-kontraktet på
// den faktiska persistensvägen (riktig Postgres jsonb i job_seekers.match_preferences
// via AppDbContext; konvertern är internal i Infrastructure och testas medvetet via
// beteendet — etablerat mönster, se SearchCriteriaJsonbBackcompatTests). Spår 3 lägger
// till en 4:e dimension PreferredMunicipalities; dessa tester låser den TOLERANTA
// läsvägen + array-skrivformen (INGEN scoring — det är PR-B):
//
//   1. KRITISK tolerans: en GAMMAL rad (skriven FÖRE municipality-nyckeln fanns,
//      eller kolumnens '{}'-default) saknar "PreferredMunicipalities" → läses som TOM
//      lista, INTE en krasch (saknad nyckel → tom lista, samma mönster som de tre
//      befintliga dimensionerna).
//   2. Skalär-OCH-array-tolerans för den nya nyckeln (ReadStringOrStringArray-återbruk).
//   3. Write emitterar municipalities som array; round-trip bevarar dem (sorterad ordinal).
//
// Seedar via JobSeeker-aggregatet (EF fyller alla obligatoriska kolumner) och muterar
// sedan ENBART match_preferences-kolumnen med rå jsonb för att simulera en godtycklig
// redan-persistent form. RÖD tills MatchPreferences + konvertern bär municipality-nyckeln.
[Collection("Api")]
public sealed class MatchPreferencesJsonbBackcompatTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly ApiFactory _factory = factory;

    // Test-isolation: rensa job_seekers så raderna inte poison:ar grann-tester i
    // [Collection("Api")] (oberoende av exekveringsordning).
    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM job_seekers;", TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private async Task<JobSeeker> SeedSeekerAsync(MatchPreferences? prefs, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Backcompat User", clock).Value;
        if (prefs is not null)
            seeker.UpdateMatchPreferences(prefs, clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    // Skriver RÅ jsonb direkt i match_preferences-kolumnen (kringgår EF-mappningen)
    // för att simulera en redan-persistent rad i godtycklig form.
    private async Task SetRawMatchPreferencesAsync(
        Guid jobSeekerId, string json, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE job_seekers SET match_preferences = @json::jsonb WHERE id = @id";
        cmd.Parameters.AddWithValue("json", json);
        cmd.Parameters.AddWithValue("id", jobSeekerId);
        await cmd.ExecuteNonQueryAsync(ct);
        await conn.CloseAsync();
    }

    // Läser match_preferences som RÅ text (verifierar Write-formen on-disk).
    private async Task<string> ReadRawMatchPreferencesAsync(Guid jobSeekerId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT match_preferences::text FROM job_seekers WHERE id = @id";
        cmd.Parameters.AddWithValue("id", jobSeekerId);
        var raw = (string)(await cmd.ExecuteScalarAsync(ct))!;
        await conn.CloseAsync();
        return raw;
    }

    private async Task<MatchPreferences> ReloadPreferencesAsync(JobSeekerId id, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.JobSeekers.SingleAsync(js => js.Id == id, ct);
        return reloaded.MatchPreferences;
    }

    // ---------------------------------------------------------------
    // (1) KRITISK tolerans — gammal rad UTAN PreferredMunicipalities-nyckel
    // läses som TOM municipality-lista (inte krasch).
    // ---------------------------------------------------------------

    [Fact]
    public async Task OldRow_WithoutMunicipalitiesKey_DeserializesToEmptyMunicipalities()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(null, ct);

        // En rad skriven FÖRE municipality-dimensionen infördes: bara de tre
        // ursprungliga nycklarna finns, "PreferredMunicipalities" saknas helt.
        var legacyJson = """
            {"PreferredOccupationGroups":["grp_12345"],"PreferredRegions":["stockholm_AB"],"PreferredEmploymentTypes":["et_fast"]}
            """;
        await SetRawMatchPreferencesAsync(seeker.Id.Value, legacyJson, ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.PreferredMunicipalities.ShouldBeEmpty();
        // De tre ursprungliga dimensionerna är oförändrade (ingen regression).
        prefs.PreferredOccupationGroups.ShouldBe(["grp_12345"]);
        prefs.PreferredRegions.ShouldBe(["stockholm_AB"]);
        prefs.PreferredEmploymentTypes.ShouldBe(["et_fast"]);
    }

    [Fact]
    public async Task BareEmptyObject_DeserializesToEmptyMunicipalities()
    {
        // Kolumnens '{}'-default (befintlig rad vid ADD COLUMN) → alla nycklar
        // saknas → MatchPreferences.Empty, inkl. tom municipality-lista.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(null, ct);
        await SetRawMatchPreferencesAsync(seeker.Id.Value, "{}", ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.PreferredMunicipalities.ShouldBeEmpty();
        prefs.PreferredOccupationGroups.ShouldBeEmpty();
        prefs.PreferredRegions.ShouldBeEmpty();
        prefs.PreferredEmploymentTypes.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // (2) Nya nyckeln tolereras i BÅDE skalär- och array-form
    // (ReadStringOrStringArray-återbruk, default-deny för övriga former).
    // ---------------------------------------------------------------

    [Fact]
    public async Task MunicipalitiesKey_ScalarForm_ReadsAsSingleElementList()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(null, ct);

        var json = """
            {"PreferredOccupationGroups":[],"PreferredRegions":[],"PreferredEmploymentTypes":[],"PreferredMunicipalities":"sthlm_kn"}
            """;
        await SetRawMatchPreferencesAsync(seeker.Id.Value, json, ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.PreferredMunicipalities.ShouldBe(["sthlm_kn"]);
    }

    [Fact]
    public async Task MunicipalitiesKey_ArrayForm_ReadsAsList()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(null, ct);

        var json = """
            {"PreferredOccupationGroups":[],"PreferredRegions":[],"PreferredEmploymentTypes":[],"PreferredMunicipalities":["sthlm_kn","gbg_kn"]}
            """;
        await SetRawMatchPreferencesAsync(seeker.Id.Value, json, ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.PreferredMunicipalities.ShouldBe(["gbg_kn", "sthlm_kn"]); // sorterad ordinal
    }

    // ---------------------------------------------------------------
    // (3) Write emitterar municipalities som array; round-trip bevarar dem.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Write_EmitsMunicipalitiesAsArray()
    {
        var ct = TestContext.Current.CancellationToken;
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: null,
            preferredRegions: null,
            preferredEmploymentTypes: null,
            preferredMunicipalities: ["sthlm_kn", "gbg_kn"]).Value;
        var seeker = await SeedSeekerAsync(prefs, ct);

        var raw = await ReadRawMatchPreferencesAsync(seeker.Id.Value, ct);

        // Nyckeln skrivs alltid (array-form). jsonb normaliserar nyckelordning
        // on-disk → bara NÄRVARO + array-formen låses här, inte ordningen.
        raw.ShouldContain("\"PreferredMunicipalities\"");
        raw.ShouldContain("sthlm_kn");
        raw.ShouldContain("gbg_kn");
    }

    [Fact]
    public async Task NewForm_WithMunicipalities_RoundTripsThroughEf()
    {
        var ct = TestContext.Current.CancellationToken;
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345"],
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast"],
            preferredMunicipalities: ["uppsala_kn", "sthlm_kn"]).Value;
        var seeker = await SeedSeekerAsync(prefs, ct);

        var reloaded = await ReloadPreferencesAsync(seeker.Id, ct);

        reloaded.PreferredOccupationGroups.ShouldBe(["grp_12345"]);
        reloaded.PreferredRegions.ShouldBe(["stockholm_AB"]);
        reloaded.PreferredEmploymentTypes.ShouldBe(["et_fast"]);
        reloaded.PreferredMunicipalities.ShouldBe(["sthlm_kn", "uppsala_kn"]); // sorterad ordinal
        reloaded.ShouldBe(prefs); // strukturell equality bevarad (inkl. municipalities)
    }
}
