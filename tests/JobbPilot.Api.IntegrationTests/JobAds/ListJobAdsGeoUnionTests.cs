using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Internal;
using JobbPilot.Application.JobAds.Queries.ListJobAds;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.JobAds;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

// Fas E2b (ADR 0067 implementerings-notat 2026-06-11, CTO VAL 1 Variant D) —
// Ort-dimensionens geografi-union: när BÅDE region- och municipality-listan är
// icke-tomma kombineras de som inkluderande UNION (kommun-träff ELLER region-
// träff), inte sekventiellt AND. Län ⊃ kommun är EN dimension i två
// granulariteter — sekventiella Where-klausuler gav noll träffar för
// region=län-X + kommun-i-län-Y (en vardaglig Platsbanken-kombination).
// Semantiken speglar JobTechs verifierade beteende (GettingStartedJobSearchEN
// — "most local promoted" = union; paritet doms på resultatmängd).
//
// Testcontainers-Postgres obligatoriskt (CTO-direktiv): filtreringen går mot
// STORED generated columns via shadow-props + Npgsql Contains-translation —
// EF InMemory beräknar inte kolumnerna och skulle ge falska gröna
// (feedback_ef_strongly_typed_vo_contains_translation). Mönster speglar
// ListJobAdsMultiFilterTests.
[Collection("Api")]
public class ListJobAdsGeoUnionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedImportedJobAdAsync(
        string title,
        string? regionConceptId,
        string? municipalityConceptId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"ext-{Guid.NewGuid():N}";

        // workplace_address bär BÅDA nycklarna (B1: region_concept_id +
        // municipality_concept_id → varsin STORED-kolumn). null-värde = nyckeln
        // utelämnas inte utan sätts till JSON null (STORED-extraktionen ger
        // NULL-kolumn) — region-only-annonsen (kommun NULL) är syntetisk:
        // korpusen har noll sådana idag, men payload-formen är legitim och
        // recall-garantin måste bevisas, inte antas (CTO-direktiv).
        var regionJson = regionConceptId is null ? "null" : $"\"{regionConceptId}\"";
        var municipalityJson = municipalityConceptId is null ? "null" : $"\"{municipalityConceptId}\"";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"workplace_address\":{{" +
            $"\"region_concept_id\":{regionJson}," +
            $"\"municipality_concept_id\":{municipalityJson}}}}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private static ListJobAdsQueryHandler CreateHandler(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new ListJobAdsQueryHandler(
            new JobAdSearchQuery(db, Substitute.For<IOccupationSynonymExpander>()),
            new SearchQueryParser());
    }

    [Fact]
    public async Task ApplyCriteria_RegionAndMunicipalityInOtherRegion_ReturnsUnionNotEmptyIntersection()
    {
        // Kärnfallet: helt län X + kommun i län Y. Sekventiellt AND hade gett
        // noll träffar (ingen annons kan ligga i både län X och kommun-i-Y) —
        // unionen ska ge båda annonserna.
        var ct = TestContext.Current.CancellationToken;
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var regY = $"reg{Guid.NewGuid():N}"[..16];
        var muniInY = $"mun{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("I län X", regX, $"mun{Guid.NewGuid():N}"[..16], ct);
        await SeedImportedJobAdAsync("I kommun i län Y", regY, muniInY, ct);
        await SeedImportedJobAdAsync("Annan kommun i län Y", regY, $"mun{Guid.NewGuid():N}"[..16], ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Region: [regX], Municipality: [muniInY]), ct);

        result.Items.Select(i => i.Title).OrderBy(t => t)
            .ShouldBe(["I kommun i län Y", "I län X"]);
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ApplyCriteria_RegionAndMunicipalityWithinSameRegion_MatchesRegionWide()
    {
        // Intra-län-fallet: helt län X + kommun i samma län X. Unionen gör
        // kommun-valet redundant (region-grenen täcker redan) — hela länets
        // annonser returneras, ingen tyst krympning till bara kommunen.
        var ct = TestContext.Current.CancellationToken;
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var muniA = $"mun{Guid.NewGuid():N}"[..16];
        var muniB = $"mun{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("Kommun A i X", regX, muniA, ct);
        await SeedImportedJobAdAsync("Kommun B i X", regX, muniB, ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Region: [regX], Municipality: [muniA]), ct);

        result.Items.Select(i => i.Title).OrderBy(t => t)
            .ShouldBe(["Kommun A i X", "Kommun B i X"]);
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ApplyCriteria_RegionOnlyAd_IsReachableViaRegionBranchInUnion()
    {
        // Recall-garantin (CTO-direktiv): syntetisk region-only-annons
        // (municipality NULL — noll sådana i dagens korpus men legitim
        // payload-form; STORED-kolumnerna är oberoende extraktioner). Den ska
        // nås via region-grenen även när kommun-filter är aktivt i unionen —
        // municipality-only-semantik (Variant B) hade tappat den tyst.
        var ct = TestContext.Current.CancellationToken;
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var muniOther = $"mun{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("Region-only i X", regX, null, ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Region: [regX], Municipality: [muniOther]), ct);

        result.Items.Select(i => i.Title).ShouldContain("Region-only i X");
    }

    [Fact]
    public async Task ApplyCriteria_MunicipalityOnlyFilter_IsUnchangedByUnionBranch()
    {
        // Regressions-grind: ensam municipality-lista går genom den
        // oförändrade enkel-grenen (IN(...) utan region-union) — exakt
        // pre-E2b-beteende (ADR 0039 Beslut 1 SPOT får ej divergera).
        var ct = TestContext.Current.CancellationToken;
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var muniA = $"mun{Guid.NewGuid():N}"[..16];
        var muniB = $"mun{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("Kommun A", regX, muniA, ct);
        await SeedImportedJobAdAsync("Kommun B", regX, muniB, ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Municipality: [muniA]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Kommun A");
    }

    [Fact]
    public async Task ApplyCriteria_GeoUnionAndOccupationGroup_RemainsAndAcrossOrthogonalDimensions()
    {
        // Ort-unionen ändrar INTE AND-semantiken mot ortogonala dimensioner
        // (yrke): (region X ∪ kommun-i-Y) AND occupationGroup. Annons i län X
        // med fel yrkesgrupp ska INTE matcha.
        var ct = TestContext.Current.CancellationToken;
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var regY = $"reg{Guid.NewGuid():N}"[..16];
        var muniInY = $"mun{Guid.NewGuid():N}"[..16];
        var grpWanted = $"grp{Guid.NewGuid():N}"[..16];
        var grpOther = $"grp{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdWithGroupAsync("Rätt yrke i X", regX, null, grpWanted, ct);
        await SeedImportedJobAdWithGroupAsync("Fel yrke i X", regX, null, grpOther, ct);
        await SeedImportedJobAdWithGroupAsync("Rätt yrke i kommun-i-Y", regY, muniInY, grpWanted, ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(
                Region: [regX],
                Municipality: [muniInY],
                OccupationGroup: [grpWanted]), ct);

        result.Items.Select(i => i.Title)
            .ShouldBe(["Rätt yrke i X", "Rätt yrke i kommun-i-Y"], ignoreOrder: true);
        result.TotalCount.ShouldBe(2);
    }

    private async Task SeedImportedJobAdWithGroupAsync(
        string title,
        string? regionConceptId,
        string? municipalityConceptId,
        string occupationGroupConceptId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"ext-{Guid.NewGuid():N}";
        var regionJson = regionConceptId is null ? "null" : $"\"{regionConceptId}\"";
        var municipalityJson = municipalityConceptId is null ? "null" : $"\"{municipalityConceptId}\"";
        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"occupation_group\":{{\"concept_id\":\"{occupationGroupConceptId}\"}}," +
            $"\"workplace_address\":{{" +
            $"\"region_concept_id\":{regionJson}," +
            $"\"municipality_concept_id\":{municipalityJson}}}}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }
}
