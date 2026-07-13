using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// ADR 0067 Beslut 5a (Fas D1) — SuggestJobAdTermsQueryHandler union-väg.
// Titel-grenen kräver riktig Postgres (EF.Functions.Like mot job_ads.Title) →
// Testcontainers via ApiFactory, [Collection("Api")]. ITaxonomyReadModel
// substitueras (NSubstitute) så taxonomi-delen är deterministisk och unionens
// ordning/dedup/cap kan asserteras isolerat mot kontrollerad indata.
//
// Verifierar: union taxonomi + titel; taxonomi FÖRE titel; dedup på
// (Kind, ConceptId ?? Label); Title-hits har ConceptId=null + Kind=Title;
// limit-cap över hela unionen. (Tom-prefix/min-2-validering ligger i
// SuggestJobAdTermsQueryValidatorTests — opåverkad av denna kontraktsändring.)
[Collection("Api")]
public class SuggestJobAdTermsUnionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task SeedTitleAsync(string title, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var ext = $"ext-{Guid.NewGuid():N}";
        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{ext}",
            external: ExternalReference.Create(JobSource.Platsbanken, ext).Value,
            rawPayload: $"{{\"id\":\"{ext}\"}}",
            facets: TestFacets.FromPayload($"{{\"id\":\"{ext}\"}}"),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

#pragma warning disable CA2012 // ValueTask-stub konsumeras av NSubstitute (jfr TaxonomyQueryHandlersTests)
    private static ITaxonomyReadModel TaxonomyReturning(
        params TaxonomySuggestionDto[] hits)
    {
        var taxonomy = Substitute.For<ITaxonomyReadModel>();
        taxonomy.SuggestByPrefixAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TaxonomySuggestionDto>>(
                (IReadOnlyList<TaxonomySuggestionDto>)hits));
        return taxonomy;
    }
#pragma warning restore CA2012

    [Fact]
    public async Task Handle_ShouldUnionTaxonomyAndTitle_WithTaxonomyFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"uni{Guid.NewGuid():N}"[..14];
        await SeedTitleAsync($"{token} utvecklare", ct);

        var taxonomy = TaxonomyReturning(
            new TaxonomySuggestionDto(SuggestionKind.Region, "r1", $"{token}-region"),
            new TaxonomySuggestionDto(SuggestionKind.OccupationGroup, "g1", $"{token}-grupp"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new SuggestJobAdTermsQueryHandler(db, taxonomy);

        var result = await handler.Handle(new SuggestJobAdTermsQuery(token, 10), ct);

        // Båda taxonomi-träffarna + titel-träffen finns.
        result.ShouldContain(s => s.Kind == SuggestionKind.Region && s.ConceptId == "r1");
        result.ShouldContain(s => s.Kind == SuggestionKind.OccupationGroup && s.ConceptId == "g1");
        result.ShouldContain(s => s.Kind == SuggestionKind.Title && s.Label == $"{token} utvecklare");

        // Taxonomi FÖRE titel: alla taxonomi-Kinds ligger före första Title.
        var firstTitleIndex = result.ToList().FindIndex(s => s.Kind == SuggestionKind.Title);
        var lastTaxonomyIndex = result.ToList().FindLastIndex(s => s.Kind != SuggestionKind.Title);
        firstTitleIndex.ShouldBeGreaterThan(lastTaxonomyIndex);
    }

    [Fact]
    public async Task Handle_ShouldSetConceptIdNullAndKindTitle_ForTitleHits()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"ttl{Guid.NewGuid():N}"[..14];
        await SeedTitleAsync($"{token} roll", ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Tom taxonomi → endast titel-grenen.
        var handler = new SuggestJobAdTermsQueryHandler(db, TaxonomyReturning());

        var result = await handler.Handle(new SuggestJobAdTermsQuery(token, 10), ct);

        var titleHit = result.ShouldHaveSingleItem();
        titleHit.Kind.ShouldBe(SuggestionKind.Title);
        titleHit.ConceptId.ShouldBeNull();
        titleHit.Label.ShouldBe($"{token} roll");
    }

    [Fact]
    public async Task Handle_ShouldDedupTaxonomyAndTitle_WhenSameKindAndKey()
    {
        // Dedup på (Kind, ConceptId ?? Label). Två taxonomi-hits med samma
        // (Kind, ConceptId) → endast en behålls.
        var ct = TestContext.Current.CancellationToken;
        var token = $"dup{Guid.NewGuid():N}"[..14];

        var taxonomy = TaxonomyReturning(
            new TaxonomySuggestionDto(SuggestionKind.Region, "same-id", $"{token}-A"),
            new TaxonomySuggestionDto(SuggestionKind.Region, "same-id", $"{token}-B"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new SuggestJobAdTermsQueryHandler(db, taxonomy);

        var result = await handler.Handle(new SuggestJobAdTermsQuery(token, 10), ct);

        result.Count(s => s.Kind == SuggestionKind.Region && s.ConceptId == "same-id")
            .ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ShouldCapTotalUnion_AtLimit()
    {
        // Limit-cap över HELA unionen (taxonomi + titel tillsammans).
        var ct = TestContext.Current.CancellationToken;
        var token = $"cap{Guid.NewGuid():N}"[..14];

        // 2 titel-träffar.
        await SeedTitleAsync($"{token}-a", ct);
        await SeedTitleAsync($"{token}-b", ct);

        // 3 taxonomi-träffar → totalt 5 kandidater, limit 3.
        var taxonomy = TaxonomyReturning(
            new TaxonomySuggestionDto(SuggestionKind.Region, "r1", $"{token}-r1"),
            new TaxonomySuggestionDto(SuggestionKind.Municipality, "m1", $"{token}-m1"),
            new TaxonomySuggestionDto(SuggestionKind.OccupationGroup, "g1", $"{token}-g1"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new SuggestJobAdTermsQueryHandler(db, taxonomy);

        var result = await handler.Handle(new SuggestJobAdTermsQuery(token, 3), ct);

        result.Count.ShouldBe(3);
        // Taxonomi prioriteras (kommer först) → de tre taxonomi-träffarna fyller cap:en.
        result.ShouldAllBe(s => s.Kind != SuggestionKind.Title);
    }
}
