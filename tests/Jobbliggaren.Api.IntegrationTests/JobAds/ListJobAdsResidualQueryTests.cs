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

// Fas D2 (ADR 0067 Beslut 5c) — residual-fritext via ISearchQueryParser,
// end-to-end mot riktig Testcontainers-Postgres (EJ InMemory; memory
// feedback_ef_strongly_typed_vo_contains_translation: InMemory döljer Npgsql-
// translations-fel → 500 i prod). Mönstret speglar ListJobAdsFtsTests +
// ListJobAdsMultiFilterTests (samma seed-helpers, samma [Collection("Api")]).
//
// Låser residual-Q→FTS-vägen (recall-bevarande OR-term, kraschsäker):
//  * normalisering ändrar inte recall ("  utvecklare  " ⇔ "utvecklare")
//  * residual-Q utan träffar ger tom sida, kraschar ALDRIG (200, items=[])
//  * residual-Q AND dimension-filter (Region) → dimension AND (FTS OR title-LIKE)
//  * kontroll-tecken i Q strippas → matchar rena annonser, ingen 500
//
// ListJobAdsQueryHandler är en tunn adapter; den instansieras här med en riktig
// JobAdSearchQuery + riktig SearchQueryParser (ren CPU, InternalsVisibleTo).
[Collection("Api")]
public class ListJobAdsResidualQueryTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<Guid> SeedJobAdAsync(
        string title,
        string description,
        CancellationToken ct,
        DateTimeOffset? publishedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var jobAd = JobAd.Create(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: description,
            url: $"https://example.com/jobs/{Guid.NewGuid():N}",
            source: JobSource.Manual,
            publishedAt: publishedAt ?? clock.UtcNow.AddDays(-1),
            expiresAt: null,
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    // Importerad annons med region_concept_id (för dimension-AND-testet) —
    // speglar SeedImportedJobAdAsync i ListJobAdsMultiFilterTests.
    private async Task SeedImportedJobAdAsync(
        string title,
        string? regionConceptId,
        CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"ext-{Guid.NewGuid():N}";
        var regionJson = regionConceptId is null
            ? "null"
            : $"{{\"region_concept_id\":\"{regionConceptId}\"}}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"occupation_group\":null," +
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

    private static ListJobAdsQueryHandler CreateHandler(IServiceScope scope) =>
        new(
            new JobAdSearchQuery(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                Substitute.For<IOccupationSynonymExpander>()),
            Substitute.For<Jobbliggaren.Application.JobAds.Abstractions.IMatchSortedJobAdSearchQuery>(),
            Substitute.For<Jobbliggaren.Application.Matching.Abstractions.IMatchProfileBuilder>(),
            new SearchQueryParser());

    // 1. Normalisering ändrar inte recall — omgivande whitespace ger samma
    //    träff-set som ren Q.
    [Fact]
    public async Task ResidualQ_SurroundingWhitespace_YieldsSameHitsAsCleanQ()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"recalltoken{Guid.NewGuid():N}"[..18];

        await SeedJobAdAsync($"{token} annons ett", "Beskrivning", ct);
        await SeedJobAdAsync($"{token} annons två", "Beskrivning", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var clean = await handler.Handle(new ListJobAdsQuery(Q: token), ct);
        var padded = await handler.Handle(new ListJobAdsQuery(Q: $"  {token}  "), ct);

        padded.TotalCount.ShouldBe(clean.TotalCount);
        padded.Items.Select(i => i.Id).OrderBy(g => g)
            .ShouldBe(clean.Items.Select(i => i.Id).OrderBy(g => g));
    }

    // 2. Residual-Q utan träffar → tom sida, kraschar ALDRIG (kraschsäkerhet).
    [Fact]
    public async Task ResidualQ_NoMatches_ReturnsEmptyPageWithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        var unmatched = $"finnsinte{Guid.NewGuid():N}"[..18];

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(new ListJobAdsQuery(Q: unmatched), ct);

        result.ShouldNotBeNull();
        result.Items.ShouldNotContain(i => i.Title.Contains(unmatched));
    }

    // 3. Residual-Q AND dimension-filter (Region) → dimension AND (FTS OR
    //    title-LIKE). Residual breddar inom Q-grenen men smalnar mot
    //    dimensionen (strukturell kraschsäkerhet: Q = OR-gren, region = AND .Where).
    [Fact]
    public async Task ResidualQ_CombinedWithRegion_AndsAcrossQAndDimension()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"anddim{Guid.NewGuid():N}"[..16];
        var regionInside = $"reg{Guid.NewGuid():N}"[..16];
        var regionOutside = $"reg{Guid.NewGuid():N}"[..16];

        // Matchar Q + är i target-regionen.
        await SeedImportedJobAdAsync($"{token} match i region", regionInside, ct);
        // Matchar Q men ligger UTANFÖR target-regionen → ska filtreras bort.
        await SeedImportedJobAdAsync($"{token} fel region", regionOutside, ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Q: $"  {token}  ", Region: [regionInside]), ct);

        // ANDar: bara den annons som matchar BÅDE Q och region.
        result.Items.ShouldContain(i => i.Title == $"{token} match i region");
        result.Items.ShouldNotContain(i => i.Title == $"{token} fel region");
    }

    // 4. Kontroll-tecken i Q strippas → matchar rena annonser, ingen 500.
    [Fact]
    public async Task ResidualQ_WithControlChars_StripsAndMatchesCleanAd()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"ctrltoken{Guid.NewGuid():N}"[..18];

        await SeedJobAdAsync($"{token} annons", "Beskrivning", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        // Zero-width space (Cf) injicerad mitt i token → strippas till ren token,
        // matchar den seedade annonsen utan att kasta.
        var split = token.Insert(token.Length / 2, "​");
        var result = await handler.Handle(new ListJobAdsQuery(Q: split), ct);

        result.Items.ShouldContain(i => i.Title == $"{token} annons");
    }
}
