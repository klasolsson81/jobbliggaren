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

// B2 (ADR 0067 Beslut 6/7 Fas B2 query-wiring) — anställningsform
// (EmploymentType) + omfattning (WorktimeExtent) görs sökbara. Speglar
// ListJobAdsOccupationGroupFilterTests mot riktig Testcontainers-Postgres
// (ALDRIG EF-InMemory: list.Contains(EF.Property<string?>(...)) → SQL IN(...)
// översätts enbart av Npgsql — InMemory ger falska gröna,
// feedback_ef_strongly_typed_vo_contains_translation).
//
// On-disk payload-paths (generated columns auto-populeras av Postgres vid INSERT).
// MEDVETET namnglapp wire-key vs kolumn (B2-kontrakt):
//   raw_payload->'employment_type'->>'concept_id'    → employment_type_concept_id   (TOP-LEVEL)
//   raw_payload->'working_hours_type'->>'concept_id' → worktime_extent_concept_id   (TOP-LEVEL, wire-key working_hours_type!)
//
// Semantik (B2): EmploymentType ⊥ WorktimeExtent — ortogonala dimensioner,
// AND mellan dem (INTE geo-union à la region/kommun). AND mot yrke består.
//
// RÖD tills ListJobAdsQuery + JobAdFilterCriteria + JobAdSearchQuery.ApplyCriteria
// + generated columns implementerar Klass 2-dimensionerna.
[Collection("Api")]
public class ListJobAdsKlass2FilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedImportedJobAdAsync(
        string title,
        string? employmentTypeConceptId,
        string? worktimeExtentConceptId,
        string? occupationGroupConceptId,
        string externalId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, employmentTypeConceptId, worktimeExtentConceptId, occupationGroupConceptId);

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

    private static string BuildRawPayload(
        string externalId,
        string? employmentTypeConceptId,
        string? worktimeExtentConceptId,
        string? occupationGroupConceptId)
    {
        // Båda Klass 2-nycklarna är TOP-LEVEL. worktime mappas från wire-key
        // "working_hours_type" (namnglappet är medvetet, B2-kontrakt).
        var employmentJson = employmentTypeConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{employmentTypeConceptId}\"}}";
        var worktimeJson = worktimeExtentConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{worktimeExtentConceptId}\"}}";
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";

        return
            $"{{\"id\":\"{externalId}\"," +
            $"\"employment_type\":{employmentJson}," +
            $"\"working_hours_type\":{worktimeJson}," +
            $"\"occupation_group\":{groupJson}}}";
    }

    private static ListJobAdsQueryHandler CreateHandler(IServiceScope scope) =>
        new(
            new JobAdSearchQuery(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                Substitute.For<IOccupationSynonymExpander>()),
            Substitute.For<Jobbliggaren.Application.JobAds.Abstractions.IPerUserJobAdSearchQuery>(),
            Substitute.For<Jobbliggaren.Application.Matching.Abstractions.IMatchProfileBuilder>(),
            new SearchQueryParser());

    // ---------------------------------------------------------------
    // (a) EmploymentType single
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyCriteria_EmploymentTypeSingle_MatchesOnlyAdWithThatType()
    {
        var ct = TestContext.Current.CancellationToken;
        var etMatch = $"et{Guid.NewGuid():N}"[..16];
        var etOther = $"et{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "Match", etMatch, null, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "EjMatch", etOther, null, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(EmploymentType: [etMatch]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Match");
    }

    // ---------------------------------------------------------------
    // (b) EmploymentType multi → IN-union
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyCriteria_EmploymentTypeMulti_MatchesUnionOfAllValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var etA = $"et{Guid.NewGuid():N}"[..16];
        var etB = $"et{Guid.NewGuid():N}"[..16];
        var etOther = $"et{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("Annons A", etA, null, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Annons B", etB, null, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Annons C", etOther, null, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(EmploymentType: [etA, etB]), ct);

        result.Items.Select(i => i.Title).OrderBy(t => t)
            .ShouldBe(["Annons A", "Annons B"]);
        result.TotalCount.ShouldBe(2);
    }

    // ---------------------------------------------------------------
    // (c) WorktimeExtent single (wire-key working_hours_type)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyCriteria_WorktimeExtentSingle_MatchesOnlyAdWithThatExtent()
    {
        var ct = TestContext.Current.CancellationToken;
        var wtMatch = $"wt{Guid.NewGuid():N}"[..16];
        var wtOther = $"wt{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "Match", null, wtMatch, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync(
            "EjMatch", null, wtOther, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(WorktimeExtent: [wtMatch]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Match");
    }

    // ---------------------------------------------------------------
    // (d) Annons utan employment_type i payload (NULL-kolumn) → ej matchad
    // ("0 träffar är inte bug" — paritet med purgad payload)
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyCriteria_EmploymentType_AdWithoutPayload_NotMatched()
    {
        var ct = TestContext.Current.CancellationToken;
        var et = $"et{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync(
            "Saknar anställningsform", employmentTypeConceptId: null, null, null,
            $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(EmploymentType: [et]), ct);

        result.TotalCount.ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // (e) EmploymentType AND WorktimeExtent — ortogonala (INTE union) →
    // bara annons som matchar BÅDA
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyCriteria_EmploymentTypeAndWorktimeExtent_AppliesAndAcrossDimensions()
    {
        var ct = TestContext.Current.CancellationToken;
        var et = $"et{Guid.NewGuid():N}"[..16];
        var wt = $"wt{Guid.NewGuid():N}"[..16];
        var etOther = $"et{Guid.NewGuid():N}"[..16];
        var wtOther = $"wt{Guid.NewGuid():N}"[..16];

        // Matchar BÅDA.
        await SeedImportedJobAdAsync("Bägge", et, wt, null, $"ext-{Guid.NewGuid():N}", ct);
        // Rätt anställningsform men fel omfattning → ej matchad (AND, ej union).
        await SeedImportedJobAdAsync("BaraEt", et, wtOther, null, $"ext-{Guid.NewGuid():N}", ct);
        // Rätt omfattning men fel anställningsform → ej matchad.
        await SeedImportedJobAdAsync("BaraWt", etOther, wt, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(EmploymentType: [et], WorktimeExtent: [wt]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Bägge");
    }

    // ---------------------------------------------------------------
    // (f) EmploymentType AND OccupationGroup — AND-semantik mot yrke
    // ---------------------------------------------------------------

    [Fact]
    public async Task ApplyCriteria_EmploymentTypeAndOccupationGroup_AppliesAndAcrossDimensions()
    {
        var ct = TestContext.Current.CancellationToken;
        var et = $"et{Guid.NewGuid():N}"[..16];
        var grp = $"grp{Guid.NewGuid():N}"[..16];
        var grpOther = $"grp{Guid.NewGuid():N}"[..16];

        // Matchar BÅDA (anställningsform OCH yrkesgrupp).
        await SeedImportedJobAdAsync("Bägge", et, null, grp, $"ext-{Guid.NewGuid():N}", ct);
        // Rätt anställningsform men fel yrkesgrupp → ej matchad.
        await SeedImportedJobAdAsync("FelYrke", et, null, grpOther, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(EmploymentType: [et], OccupationGroup: [grp]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Bägge");
    }
}
