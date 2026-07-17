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

// Batch 5 — ADR 0042 Beslut C (typeahead C1). Verifierar mot riktig Postgres:
// case-insensitiv left-anchored ILIKE-prefix, Distinct, Take-cap, LIKE-
// metateckens-escaping (left-anchor bevarad). SuggestJobAdTermsQueryHandler är
// public path; index (btree functional lower(title) text_pattern_ops partial)
// är prestanda, korrekthet verifieras här oavsett index.
//
// ADR 0067 Beslut 5a (Fas D1) — kontraktsändring: handlern tar nu även
// ITaxonomyReadModel och returnerar IReadOnlyList<SuggestionDto> (ej string).
// Dessa befintliga titel-grens-tester ISOLERAS till titel-vägen genom att
// substituera en TOM taxonomi-port (SuggestByPrefixAsync → []), så att
// ursprunglig test-intention (case-insensitiv prefix, Distinct, Limit-cap,
// LIKE-escape på titlar) bevaras oförändrad. Union-/taxonomi-grenen täcks av
// SuggestJobAdTermsUnionTests + TaxonomyReadModelSuggestTests.
[Collection("Api")]
public class SuggestJobAdTermsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Tom taxonomi-port: union-handlern ser inga taxonomi-träffar → resultatet
    // består enbart av titel-grenen (vad dessa tester verifierar). #pragma: se
    // TaxonomyQueryHandlersTests för CA2012-rationalen (ValueTask-stub).
#pragma warning disable CA2012
    private static ITaxonomyReadModel EmptyTaxonomy()
    {
        var taxonomy = Substitute.For<ITaxonomyReadModel>();
        taxonomy.SuggestByPrefixAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TaxonomySuggestionDto>>(
                (IReadOnlyList<TaxonomySuggestionDto>)[]));
        return taxonomy;
    }
#pragma warning restore CA2012

    private async Task SeedAsync(string title, CancellationToken ct)
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
            clock: clock, declaredContacts: []).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Suggest_PrefixMatch_IsCaseInsensitive_Distinct_AndCapped()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"Zeta{Guid.NewGuid():N}"[..18];

        await SeedAsync(token, ct);
        await SeedAsync(token, ct);                       // dubblett → Distinct
        await SeedAsync($"{token} utvecklare", ct);
        await SeedAsync($"Helt annan {Guid.NewGuid():N}"[..20], ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new SuggestJobAdTermsQueryHandler(db, EmptyTaxonomy());

        // Gement prefix matchar versalt seedat (case-insensitivt via lower()).
        var result = await handler.Handle(
            new SuggestJobAdTermsQuery(token.ToLowerInvariant()[..6], 10), ct);

        // Titel-träffar har Kind=Title + ConceptId=null (ADR 0067 Beslut 5a).
        var labels = result.Select(s => s.Label).ToList();
        labels.ShouldContain(token);
        labels.ShouldContain($"{token} utvecklare");
        labels.Count(t => t == token).ShouldBe(1);        // Distinct
        labels.ShouldNotContain(t => t.StartsWith("Helt annan"));
        result.ShouldAllBe(s => s.Kind == SuggestionKind.Title && s.ConceptId == null);
    }

    [Fact]
    public async Task Suggest_RespectsLimitCap()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"cap{Guid.NewGuid():N}"[..14];
        for (var i = 0; i < 5; i++)
            await SeedAsync($"{token}-{i}", ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new SuggestJobAdTermsQueryHandler(db, EmptyTaxonomy());

        var result = await handler.Handle(new SuggestJobAdTermsQuery(token, 3), ct);

        result.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Suggest_EscapesLikeWildcards_LeftAnchorPreserved()
    {
        var ct = TestContext.Current.CancellationToken;
        var marker = TestIds.Token(10);
        // Titel som börjar med literal "%"+marker; och en utan.
        await SeedAsync($"%{marker} literal", ct);
        await SeedAsync($"prefix{marker} annan", ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new SuggestJobAdTermsQueryHandler(db, EmptyTaxonomy());

        // Prefix "%<marker>" ska tolkas LITERALT (escapad %), inte som
        // wildcard som matchar "prefix<marker>...". Endast literal-titeln.
        var result = await handler.Handle(
            new SuggestJobAdTermsQuery($"%{marker}", 10), ct);

        var labels = result.Select(s => s.Label).ToList();
        labels.ShouldContain($"%{marker} literal");
        labels.ShouldNotContain(t => t.StartsWith("prefix"));
    }
}
