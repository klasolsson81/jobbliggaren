using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Application.JobAds.Queries.SuggestJobAdTerms;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

// Batch 5 — ADR 0042 Beslut C (typeahead C1). Verifierar mot riktig Postgres:
// case-insensitiv left-anchored ILIKE-prefix, Distinct, Take-cap, LIKE-
// metateckens-escaping (left-anchor bevarad). SuggestJobAdTermsQueryHandler är
// public path; index (btree functional lower(title) text_pattern_ops partial)
// är prestanda, korrekthet verifieras här oavsett index.
[Collection("Api")]
public class SuggestJobAdTermsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

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
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;
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
        var handler = new SuggestJobAdTermsQueryHandler(db);

        // Gement prefix matchar versalt seedat (case-insensitivt via lower()).
        var result = await handler.Handle(
            new SuggestJobAdTermsQuery(token.ToLowerInvariant()[..6], 10), ct);

        result.ShouldContain(token);
        result.ShouldContain($"{token} utvecklare");
        result.Count(t => t == token).ShouldBe(1);        // Distinct
        result.ShouldNotContain(t => t.StartsWith("Helt annan"));
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
        var handler = new SuggestJobAdTermsQueryHandler(db);

        var result = await handler.Handle(new SuggestJobAdTermsQuery(token, 3), ct);

        result.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Suggest_EscapesLikeWildcards_LeftAnchorPreserved()
    {
        var ct = TestContext.Current.CancellationToken;
        var marker = Guid.NewGuid().ToString("N")[..10];
        // Titel som börjar med literal "%"+marker; och en utan.
        await SeedAsync($"%{marker} literal", ct);
        await SeedAsync($"prefix{marker} annan", ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new SuggestJobAdTermsQueryHandler(db);

        // Prefix "%<marker>" ska tolkas LITERALT (escapad %), inte som
        // wildcard som matchar "prefix<marker>...". Endast literal-titeln.
        var result = await handler.Handle(
            new SuggestJobAdTermsQuery($"%{marker}", 10), ct);

        result.ShouldContain($"%{marker} literal");
        result.ShouldNotContain(t => t.StartsWith("prefix"));
    }
}
