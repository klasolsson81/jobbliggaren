using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// C1 (ADR 0067 Platsbanken sök-paritet) — Variant C nivåbyte. yrke-filtret
// targetar OccupationGroupConceptId (ssyk-level-4, occupation_group_concept_id)
// i stället för SsykConceptId (occupation-name). Plus ny Municipality-dimension
// (municipality_concept_id). Verifierar handler-vägen mot riktig
// Testcontainers-Postgres (ALDRIG EF-InMemory — Npgsql Contains-mot-shadow-prop-
// generated-column-translation maskeras där, feedback_ef_strongly_typed_vo).
//
// ARCHITECT-FLAGGAD BLOCKERANDE GATE: List<string>.Contains(EF.Property<string?>(
//   j, "OccupationGroupConceptId")) → SQL IN(...). Samma mönster som
// ListJobAdsMultiFilterTests verifierade för Ssyk/Region.
//
// On-disk payload-paths (generated columns auto-populeras av Postgres vid INSERT):
//   raw_payload->'occupation_group'->>'concept_id'           → occupation_group_concept_id  (TOP-LEVEL)
//   raw_payload->'workplace_address'->>'municipality_concept_id' → municipality_concept_id
//   raw_payload->'workplace_address'->>'region_concept_id'   → region_concept_id
//
// RÖD tills ListJobAdsQuery + JobAdFilterCriteria + JobAdSearchQuery.ApplyCriteria
// implementerar OccupationGroup/Municipality-dimensionerna.
[Collection("Api")]
public class ListJobAdsOccupationGroupFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Seed:ar Imported JobAd med occupation_group (top-level) + municipality +
    // region på korrekt JSON-path. Generated columns auto-populeras av Postgres.
    private async Task SeedImportedJobAdAsync(
        string title,
        string? occupationGroupConceptId,
        string? municipalityConceptId,
        string? regionConceptId,
        string externalId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, municipalityConceptId, regionConceptId);

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private static string BuildRawPayload(
        string externalId,
        string? occupationGroupConceptId,
        string? municipalityConceptId,
        string? regionConceptId)
    {
        // occupation_group är TOP-LEVEL i payloaden (EJ nested under occupation).
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";

        // municipality_concept_id + region_concept_id ligger under workplace_address.
        var addressFields = new List<string>();
        if (municipalityConceptId is not null)
            addressFields.Add($"\"municipality_concept_id\":\"{municipalityConceptId}\"");
        if (regionConceptId is not null)
            addressFields.Add($"\"region_concept_id\":\"{regionConceptId}\"");
        var addressJson = addressFields.Count == 0
            ? "null"
            : $"{{{string.Join(",", addressFields)}}}";

        return
            $"{{\"id\":\"{externalId}\",\"occupation_group\":{groupJson}," +
            $"\"workplace_address\":{addressJson}}}";
    }

    private static ListJobAdsQueryHandler CreateHandler(IServiceScope scope) =>
        new(
            new JobAdSearchQuery(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                Substitute.For<IOccupationSynonymExpander>()),
            Substitute.For<Jobbliggaren.Application.JobAds.Abstractions.IPerUserJobAdSearchQuery>(),
            Substitute.For<Jobbliggaren.Application.Matching.Abstractions.IMatchProfileBuilder>(),
            new SearchQueryParser(),
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Substitute.For<Jobbliggaren.Application.Common.Abstractions.ICurrentUser>());

    // ---------------------------------------------------------------
    // OccupationGroup — nivåbyte (kritiskt)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyCriteria_OccupationGroupSingle_MatchesOnlyAdWithThatGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupMatch = $"grp{Guid.NewGuid():N}"[..16];
        var groupOther = $"grp{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "Match", groupMatch, null, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "EjMatch", groupOther, null, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(OccupationGroup: [groupMatch]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Match");
    }

    [Fact]
    public async Task ApplyCriteria_OccupationGroupMulti_MatchesUnionOfAllValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupA = $"grp{Guid.NewGuid():N}"[..16];
        var groupB = $"grp{Guid.NewGuid():N}"[..16];
        var groupOther = $"grp{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("Annons A", groupA, null, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Annons B", groupB, null, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Annons C", groupOther, null, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        // Multi-värde ⇒ IN(groupA, groupB) → UNION-match (Npgsql Contains-mot-
        // shadow-prop-translation: den arkitekt-flaggade gaten).
        var result = await handler.Handle(
            new ListJobAdsQuery(OccupationGroup: [groupA, groupB]), ct);

        result.Items.Select(i => i.Title).OrderBy(t => t)
            .ShouldBe(["Annons A", "Annons B"]);
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ApplyCriteria_OccupationGroupEmptyList_AppliesNoFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = $"grp{Guid.NewGuid():N}"[..16];
        await SeedImportedJobAdAsync(
            $"Oavsett {Guid.NewGuid():N}", group, null, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(new ListJobAdsQuery(OccupationGroup: []), ct);

        // Tom lista = inget filter → minst den seedade annonsen återfinns.
        result.TotalCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ApplyCriteria_OccupationGroup_AdWithoutPayload_NotMatched()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = $"grp{Guid.NewGuid():N}"[..16];

        // Annons utan occupation_group i payload → NULL-kolumn → ej matchad.
        await SeedImportedJobAdAsync(
            "Saknar grupp", occupationGroupConceptId: null, null, null,
            $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(OccupationGroup: [group]), ct);

        result.TotalCount.ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // Municipality — analogt Region
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyCriteria_MunicipalitySingle_MatchesOnlyAdWithThatMunicipality()
    {
        var ct = TestContext.Current.CancellationToken;
        var knMatch = $"kn{Guid.NewGuid():N}"[..16];
        var knOther = $"kn{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "Match", null, knMatch, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "EjMatch", null, knOther, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Municipality: [knMatch]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Match");
    }

    [Fact]
    public async Task ApplyCriteria_MunicipalityMulti_MatchesUnionOfAllValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var knA = $"kn{Guid.NewGuid():N}"[..16];
        var knB = $"kn{Guid.NewGuid():N}"[..16];
        var knOther = $"kn{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("Kn A", null, knA, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Kn B", null, knB, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Kn C", null, knOther, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Municipality: [knA, knB]), ct);

        result.Items.Select(i => i.Title).OrderBy(t => t)
            .ShouldBe(["Kn A", "Kn B"]);
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ApplyCriteria_Municipality_AdWithoutPayload_NotMatched()
    {
        var ct = TestContext.Current.CancellationToken;
        var kn = $"kn{Guid.NewGuid():N}"[..16];

        // Annons utan workplace_address i payload → NULL-kolumn → ej matchad.
        await SeedImportedJobAdAsync(
            "Saknar kommun", null, municipalityConceptId: null, null,
            $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(new ListJobAdsQuery(Municipality: [kn]), ct);

        result.TotalCount.ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // Kombination — yrke AND ort; ort = union region∪kommun (Fas E2b,
    // ADR 0067 impl-notat 2026-06-11: län ⊃ kommun är EN dimension i två
    // granulariteter — geo-union ersatte det tidigare sekventiella AND:et
    // mellan region- och municipality-listorna).
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyCriteria_OccupationGroupAndGeoUnion_AppliesAndAgainstOrtUnion()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpA = $"grp{Guid.NewGuid():N}"[..16];
        var grpB = $"grp{Guid.NewGuid():N}"[..16];
        var grpOther = $"grp{Guid.NewGuid():N}"[..16];
        var knX = $"kn{Guid.NewGuid():N}"[..16];
        var knY = $"kn{Guid.NewGuid():N}"[..16];
        var region = $"reg{Guid.NewGuid():N}"[..16];

        // Matchar: grupp i {A,B} OCH (kommun i {X,Y} ELLER region).
        await SeedImportedJobAdAsync("Match1", grpA, knX, region, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Match2", grpB, knY, region, $"ext-{Guid.NewGuid():N}", ct);
        // Kommun i {X,Y} men annan region → matchar via kommun-grenen (union).
        await SeedImportedJobAdAsync(
            "ViaKommun", grpA, knX, $"reg{Guid.NewGuid():N}"[..16], $"ext-{Guid.NewGuid():N}", ct);
        // Region ok men kommun utanför {X,Y} → matchar via region-grenen (union).
        await SeedImportedJobAdAsync(
            "ViaRegion", grpA, $"kn{Guid.NewGuid():N}"[..16], region, $"ext-{Guid.NewGuid():N}", ct);
        // Ort ok (region) men FEL yrkesgrupp → matchar EJ (AND mot yrke består).
        await SeedImportedJobAdAsync(
            "FelYrke", grpOther, knX, region, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(new ListJobAdsQuery(
            OccupationGroup: [grpA, grpB],
            Municipality: [knX, knY],
            Region: [region]), ct);

        result.Items.Select(i => i.Title)
            .ShouldBe(["Match1", "Match2", "ViaKommun", "ViaRegion"], ignoreOrder: true);
        result.TotalCount.ShouldBe(4);
    }
}
