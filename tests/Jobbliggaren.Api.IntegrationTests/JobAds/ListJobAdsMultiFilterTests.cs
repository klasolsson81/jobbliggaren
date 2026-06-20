using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// Batch 3 — ADR 0042 Beslut B. ApplyCriteria (delad sök-SPOT, ADR 0039
// Beslut 1) utvidgas till list-form: multi-occupationGroup/region ⇒ IN(...) via
// occupationGroup.Contains(EF.Property<string?>(j,"OccupationGroupConceptId")).
//
// C1 (ADR 0067 Platsbanken sök-paritet) — Variant C nivåbyte: yrke-filtrets
// multi-value-tester targetar OccupationGroupConceptId (Ssyk-grenen borttagen,
// no-op-regressionen ligger i ListJobAdsSsykNoOpTests). Region oförändrad.
//
// ARCHITECT-FLAGGAD BLOCKERANDE GATE: verifierar att Npgsql översätter
// List<string>.Contains(EF.Property<string?>(...)) mot shadow-property (Postgres
// generated column) till SQL IN(...). Kan EJ verifieras mot EF in-memory
// (shadow-prop + Contains-translation saknas där) → mönster speglar
// ListJobAdsFilterTests (riktig Testcontainers-Postgres, F2P9JobAdSearchColumns
// aktiv).
//
// ADR 0062 — sök-kompositionen bor i Infrastructure-impl:en JobAdSearchQuery
// (internal, InternalsVisibleTo Api.IntegrationTests). ListJobAdsQueryHandler
// är en tunn adapter; den instansieras här med en riktig JobAdSearchQuery mot
// Testcontainers-Postgres så hela filter-vägen exekveras.
[Collection("Api")]
public class ListJobAdsMultiFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedImportedJobAdAsync(
        string title,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string externalId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        // occupation_group är TOP-LEVEL i payloaden (→ occupation_group_concept_id).
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";
        var regionJson = regionConceptId is null
            ? "null"
            : $"{{\"region_concept_id\":\"{regionConceptId}\"}}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"occupation_group\":{groupJson}," +
            $"\"workplace_address\":{regionJson}}}";

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

    [Fact]
    public async Task ApplyCriteria_MultiOccupationGroup_MatchesUnionOfAllValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var groupA = $"grp{Guid.NewGuid():N}"[..16];
        var groupB = $"grp{Guid.NewGuid():N}"[..16];
        var groupOther = $"grp{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("Annons A", groupA, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Annons B", groupB, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Annons C", groupOther, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new ListJobAdsQueryHandler(
            new JobAdSearchQuery(db, Substitute.For<IOccupationSynonymExpander>()),
            Substitute.For<IMatchSortedJobAdSearchQuery>(),
            Substitute.For<IMatchProfileBuilder>(),
            new SearchQueryParser());

        // Multi-värde ⇒ IN(groupA, groupB) → UNION-match (Npgsql Contains-mot-
        // shadow-prop-translation: den arkitekt-flaggade gaten).
        var result = await handler.Handle(
            new ListJobAdsQuery(OccupationGroup: [groupA, groupB]), ct);

        result.Items.Select(i => i.Title).OrderBy(t => t)
            .ShouldBe(["Annons A", "Annons B"]);
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ApplyCriteria_MultiRegion_MatchesUnionOfAllValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var regA = $"reg{Guid.NewGuid():N}"[..16];
        var regB = $"reg{Guid.NewGuid():N}"[..16];
        var regOther = $"reg{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("Reg A", null, regA, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Reg B", null, regB, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Reg C", null, regOther, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new ListJobAdsQueryHandler(
            new JobAdSearchQuery(db, Substitute.For<IOccupationSynonymExpander>()),
            Substitute.For<IMatchSortedJobAdSearchQuery>(),
            Substitute.For<IMatchProfileBuilder>(),
            new SearchQueryParser());

        var result = await handler.Handle(
            new ListJobAdsQuery(Region: [regA, regB]), ct);

        result.Items.Select(i => i.Title).OrderBy(t => t)
            .ShouldBe(["Reg A", "Reg B"]);
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ApplyCriteria_SingleElementOccupationGroupList_MatchesSameAsOldSingleValue()
    {
        // Regressions-grind: single-element-lista ⇒ identiskt beteende som
        // gammalt single-värde (ADR 0039 Beslut 1 SPOT får ej divergera).
        var ct = TestContext.Current.CancellationToken;
        var group = $"grp{Guid.NewGuid():N}"[..16];
        var other = $"grp{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("Match", group, null, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("EjMatch", other, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new ListJobAdsQueryHandler(
            new JobAdSearchQuery(db, Substitute.For<IOccupationSynonymExpander>()),
            Substitute.For<IMatchSortedJobAdSearchQuery>(),
            Substitute.For<IMatchProfileBuilder>(),
            new SearchQueryParser());

        var result = await handler.Handle(new ListJobAdsQuery(OccupationGroup: [group]), ct);

        result.TotalCount.ShouldBe(1);
        result.Items[0].Title.ShouldBe("Match");
    }

    [Fact]
    public async Task ApplyCriteria_EmptyOccupationGroupList_AppliesNoFilter()
    {
        // Tom lista = inget filter (generaliserad tom-invariant, ADR 0042 B.3).
        var ct = TestContext.Current.CancellationToken;
        var group = $"grp{Guid.NewGuid():N}"[..16];
        await SeedImportedJobAdAsync(
            $"Oavsett {Guid.NewGuid():N}", group, null, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new ListJobAdsQueryHandler(
            new JobAdSearchQuery(db, Substitute.For<IOccupationSynonymExpander>()),
            Substitute.For<IMatchSortedJobAdSearchQuery>(),
            Substitute.For<IMatchProfileBuilder>(),
            new SearchQueryParser());

        var result = await handler.Handle(new ListJobAdsQuery(OccupationGroup: []), ct);

        // Inget occupationGroup-filter applicerat ⇒ minst den seedade annonsen
        // återfinns (totalCount speglar ofiltrerad mängd, inte 0).
        result.TotalCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ApplyCriteria_MultiOccupationGroupAndMultiRegion_AppliesAndAcrossListsOrWithin()
    {
        // occupationGroup IN(a,b) AND region IN(x,y): OR inom lista, AND mellan listor.
        var ct = TestContext.Current.CancellationToken;
        var groupA = $"grp{Guid.NewGuid():N}"[..16];
        var groupB = $"grp{Guid.NewGuid():N}"[..16];
        var regX = $"reg{Guid.NewGuid():N}"[..16];
        var regY = $"reg{Guid.NewGuid():N}"[..16];
        var regZ = $"reg{Guid.NewGuid():N}"[..16];

        // Matchar: grupp i {A,B} OCH region i {X,Y}
        await SeedImportedJobAdAsync("Match1", groupA, regX, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("Match2", groupB, regY, $"ext-{Guid.NewGuid():N}", ct);
        // grupp ok men region utanför {X,Y}
        await SeedImportedJobAdAsync("NoRegion", groupA, regZ, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new ListJobAdsQueryHandler(
            new JobAdSearchQuery(db, Substitute.For<IOccupationSynonymExpander>()),
            Substitute.For<IMatchSortedJobAdSearchQuery>(),
            Substitute.For<IMatchProfileBuilder>(),
            new SearchQueryParser());

        var result = await handler.Handle(
            new ListJobAdsQuery(OccupationGroup: [groupA, groupB], Region: [regX, regY]), ct);

        result.Items.Select(i => i.Title).OrderBy(t => t)
            .ShouldBe(["Match1", "Match2"]);
        result.TotalCount.ShouldBe(2);
    }

    // Batch 4-relevanstestet (RelevanceSort_RanksTitleMatchFirst_AndIsNewReflectsSince)
    // togs bort i ADR 0062 FTS-skiftet: dess exakt/prefix/contains-ordnings-
    // assertion vilade på den gamla ILIKE-3-2-1-heuristiken (ADR 0042 D2) som
    // ersattes av ts_rank. Relevans-rankning + IsNew/Since täcks nu av
    // ListJobAdsFtsTests (FTS-/ts_rank-medvetna assertions mot riktig Postgres).
}
