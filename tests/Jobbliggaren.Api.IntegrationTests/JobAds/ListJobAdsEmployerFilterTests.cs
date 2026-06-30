using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// #311 D6 (följ arbetsgivare, ADR 0087) — arbetsgivar-facet: org.nr görs sökbar
// som en IN-equality-dimension. Speglar ListJobAdsKlass2FilterTests mot riktig
// Testcontainers-Postgres (ALDRIG EF-InMemory: list.Contains(EF.Property<string?>(...))
// → SQL IN(...) översätts enbart av Npgsql — InMemory ger falska gröna,
// feedback_ef_strongly_typed_vo_contains_translation).
//
// On-disk payload-path (generated column auto-populeras av Postgres vid INSERT).
// SKILJER sig från Klass 2: org.nr är NESTED under employer (EJ top-level som
// employment_type/occupation_group):
//   raw_payload->'employer'->>'organization_number' → organization_number
//
// Semantik (ADR 0087 D6, speglar ADR 0067 Beslut 6): Employer är en ORTOGONAL
// dimension — enkel IN-equality, AND mot allt annat (INTE geo-union à la
// region/kommun). org.nr = den KANONISKA följ-nyckeln (ingen fuzzy namn-matchning,
// "Volvo×20"-fällan). Annons utan org.nr i payload (NULL-kolumn) matchas ej
// ("0 träffar är inte bug" — paritet med övriga taxonomi-dims).
//
// RÖD tills ListJobAdsQuery + JobAdFilterCriteria + JobAdSearchComposition.ApplyFilter
// implementerar Employer-dimensionen.
[Collection("Api")]
public class ListJobAdsEmployerFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedImportedJobAdAsync(
        string title,
        string? organizationNumber,
        string? occupationGroupConceptId,
        string companyName,
        string externalId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, organizationNumber, occupationGroupConceptId, companyName);

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create(companyName).Value,
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

    private static string BuildRawPayload(
        string externalId,
        string? organizationNumber,
        string? occupationGroupConceptId,
        string companyName)
    {
        // org.nr är NESTED under employer (#311-kontrakt). employer-blocket bär
        // name + (valfritt) organization_number. organizationNumber == null
        // speglar B2-era-payloaden: employer finns men bär bara name.
        var employerJson = organizationNumber is null
            ? $"{{\"name\":\"{companyName}\"}}"
            : $"{{\"name\":\"{companyName}\",\"organization_number\":\"{organizationNumber}\"}}";
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";

        return
            $"{{\"id\":\"{externalId}\"," +
            $"\"occupation_group\":{groupJson}," +
            $"\"employer\":{employerJson}}}";
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

    // Distinkta 10-siffriga org.nr per test (undvik kollision i den delade Api-DB:n,
    // reference_api_integration_shared_db_contamination). Stabil prefix + suffix-rand.
    private static string NewOrgNr() => "55" + Random.Shared.Next(10_000_000, 99_999_999);

    // ---------------------------------------------------------------
    // (a) Employer single — bara annonsen med den org.nr:n
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyFilter_EmployerSingle_MatchesOnlyAdWithThatOrgNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgMatch = NewOrgNr();
        var orgOther = NewOrgNr();

        await SeedImportedJobAdAsync(
            "Match", orgMatch, null, "Volvo Cars AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "EjMatch", orgOther, null, "Volvo Bussar AB", $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Employer: [orgMatch]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Match");
    }

    // ---------------------------------------------------------------
    // (b) Employer multi → IN-union (flera bevakade arbetsgivare)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyFilter_EmployerMulti_MatchesUnionOfAllOrgNumbers()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgA = NewOrgNr();
        var orgB = NewOrgNr();
        var orgOther = NewOrgNr();

        await SeedImportedJobAdAsync("Annons A", orgA, null, "Företag A AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Annons B", orgB, null, "Företag B AB", $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Annons C", orgOther, null, "Företag C AB", $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Employer: [orgA, orgB]), ct);

        result.Items.Select(i => i.Title).OrderBy(t => t)
            .ShouldBe(["Annons A", "Annons B"]);
        result.TotalCount.ShouldBe(2);
    }

    // ---------------------------------------------------------------
    // (c) Annons utan org.nr i payload (NULL-kolumn) → ej matchad
    // (B2-reality: 100% av befintliga rader är NULL tills re-ingest;
    //  "0 träffar är inte bug")
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyFilter_Employer_AdWithoutOrgNumber_NotMatched()
    {
        var ct = TestContext.Current.CancellationToken;
        var org = NewOrgNr();

        await SeedImportedJobAdAsync(
            "Saknar org.nr", organizationNumber: null, null, "Gammal Annons AB",
            $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Employer: [org]), ct);

        result.TotalCount.ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // (d) Employer AND OccupationGroup — ortogonal AND-semantik mot yrke
    // (org.nr är INTE en geo-union; AND mot allt annat)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyFilter_EmployerAndOccupationGroup_AppliesAndAcrossDimensions()
    {
        var ct = TestContext.Current.CancellationToken;
        var org = NewOrgNr();
        var grp = $"grp{Guid.NewGuid():N}"[..16];
        var grpOther = $"grp{Guid.NewGuid():N}"[..16];

        // Matchar BÅDA (org.nr OCH yrkesgrupp).
        await SeedImportedJobAdAsync("Bägge", org, grp, "Volvo Cars AB", $"ext-{Guid.NewGuid():N}", ct);
        // Rätt org.nr men fel yrkesgrupp → ej matchad (AND, ej union).
        await SeedImportedJobAdAsync("FelYrke", org, grpOther, "Volvo Cars AB", $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Employer: [org], OccupationGroup: [grp]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Bägge");
    }
}
