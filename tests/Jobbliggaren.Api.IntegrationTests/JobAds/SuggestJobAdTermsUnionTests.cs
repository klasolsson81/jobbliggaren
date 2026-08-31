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
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
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


    /// <summary>
    /// #1546 — seeds ONE Active ad for a distinct legal entity. The org.nr reaches the STORED
    /// generated column through <c>raw_payload</c>, exactly as ingestion writes it; nothing here is a
    /// hand-written column value (CLAUDE.md §5 <c>Tests:</c>).
    /// </summary>
    private async Task SeedEmployerAdAsync(
        string organizationNumber, string companyName, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var ext = $"ext-{Guid.NewGuid():N}";
        var raw =
            $"{{\"id\":\"{ext}\",\"employer\":{{\"name\":\"{companyName}\","
            + $"\"organization_number\":\"{organizationNumber}\"}}}}";
        var jobAd = JobAd.Import(
            title: "Utvecklare",
            company: Company.Create(companyName).Value,
            description: "desc",
            url: $"https://example.com/jobs/{ext}",
            external: ExternalReference.Create(JobSource.Platsbanken, ext).Value,
            rawPayload: raw,
            facets: TestFacets.FromPayload(raw),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<SuggestionDto>> RunAsync(
        ITaxonomyReadModel taxonomy, string prefix, int limit, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new SuggestJobAdTermsQueryHandler(
            db, taxonomy,
            // #1546 — den RIKTIGA porten ur containern, aldrig en stub.
            scope.ServiceProvider.GetRequiredService<IEmployerDisambiguationQuery>());
        return await handler.Handle(new SuggestJobAdTermsQuery(prefix, limit), ct);
    }

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
        var handler = new SuggestJobAdTermsQueryHandler(
            db, taxonomy,
            // #1546 — den RIKTIGA porten ur containern, aldrig en stub: en
            // contains-matchning mot en fake bevisar ingenting om det som gör
            // arbetsgivar-blocket farligt (senior-cto-advisor 2026-08-31).
            scope.ServiceProvider.GetRequiredService<IEmployerDisambiguationQuery>());

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
        var handler = new SuggestJobAdTermsQueryHandler(
            db, TaxonomyReturning(),
            // #1546 — den RIKTIGA porten ur containern, aldrig en stub: en
            // contains-matchning mot en fake bevisar ingenting om det som gör
            // arbetsgivar-blocket farligt (senior-cto-advisor 2026-08-31).
            scope.ServiceProvider.GetRequiredService<IEmployerDisambiguationQuery>());

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
        var handler = new SuggestJobAdTermsQueryHandler(
            db, taxonomy,
            // #1546 — den RIKTIGA porten ur containern, aldrig en stub: en
            // contains-matchning mot en fake bevisar ingenting om det som gör
            // arbetsgivar-blocket farligt (senior-cto-advisor 2026-08-31).
            scope.ServiceProvider.GetRequiredService<IEmployerDisambiguationQuery>());

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
        var handler = new SuggestJobAdTermsQueryHandler(
            db, taxonomy,
            // #1546 — den RIKTIGA porten ur containern, aldrig en stub: en
            // contains-matchning mot en fake bevisar ingenting om det som gör
            // arbetsgivar-blocket farligt (senior-cto-advisor 2026-08-31).
            scope.ServiceProvider.GetRequiredService<IEmployerDisambiguationQuery>());

        var result = await handler.Handle(new SuggestJobAdTermsQuery(token, 3), ct);

        result.Count.ShouldBe(3);
        // Taxonomi prioriteras (kommer först) → de tre taxonomi-träffarna fyller cap:en.
        result.ShouldAllBe(s => s.Kind != SuggestionKind.Title);
    }

    // ═══════════ #1546 — the employer block: its budget, its position, its exclusion ═══════════
    // Every fact below distinguishes the CHOSEN design from a specific rejected one
    // (senior-cto-advisor 2026-08-31). A test that only asserted "an employer appears" would be
    // green under all of them.

    /// <summary>
    /// Distinguishes the chosen budget from "no per-kind cap" (the delivered accident). Five distinct
    /// employers match; the block must yield exactly three.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldCapEmployerBlock_AtItsOwnBudget_WhenMoreMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"bud{Guid.NewGuid():N}"[..14];

        for (var i = 1; i <= 5; i++)
            await SeedEmployerAdAsync($"55660707{i:D2}", $"{token} Bolag {i} AB", ct);

        var result = await RunAsync(TaxonomyReturning(), token, 10, ct);

        result.Count(x => x.Kind == SuggestionKind.Employer).ShouldBe(3,
            "the employer block matches %contains% over the whole corpus and ranks by ad count, so "
            + "without its own budget a common fragment would push out every title and taxonomy "
            + "suggestion. Hard-coded 3 on purpose: reading the constant would assert 3 == 3.");
    }

    /// <summary>
    /// The ONLY fact that distinguishes a budget on the NEW block from budgets on every block. The
    /// taxonomy assertion is the load-bearing half: a symmetric design would cap it too.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldNotBudgetTaxonomy_WhenEmployerBlockIsBudgeted()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"sym{Guid.NewGuid():N}"[..14];

        for (var i = 1; i <= 5; i++)
            await SeedEmployerAdAsync($"55660808{i:D2}", $"{token} Bolag {i} AB", ct);

        var taxonomy = TaxonomyReturning(
            new TaxonomySuggestionDto(SuggestionKind.Region, "sr1", $"{token}-1"),
            new TaxonomySuggestionDto(SuggestionKind.Region, "sr2", $"{token}-2"),
            new TaxonomySuggestionDto(SuggestionKind.Region, "sr3", $"{token}-3"),
            new TaxonomySuggestionDto(SuggestionKind.Region, "sr4", $"{token}-4"),
            new TaxonomySuggestionDto(SuggestionKind.Region, "sr5", $"{token}-5"));

        var result = await RunAsync(taxonomy, token, 10, ct);

        result.Count(x => x.Kind == SuggestionKind.Region).ShouldBe(5,
            "taxonomy keeps the priority ADR 0067 Beslut 5a gave it. #1546 budgets the NEW block "
            + "only; capping taxonomy too would re-open a delivered decision this issue never asked "
            + "to change.");
        result.Count(x => x.Kind == SuggestionKind.Employer).ShouldBe(3);
    }

    /// <summary>
    /// Position, asserted positionally rather than as "employer is present" — the typeahead paints a
    /// flat list in API order and never re-sorts, so this order IS the rendered order.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldOrderEmployerBetweenTaxonomyAndTitle()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"ord{Guid.NewGuid():N}"[..14];

        await SeedEmployerAdAsync("5566090901", $"{token} Bolag AB", ct);
        await SeedTitleAsync($"{token} utvecklare", ct);

        var taxonomy = TaxonomyReturning(
            new TaxonomySuggestionDto(SuggestionKind.Region, "or1", $"{token}-region"));

        var result = await RunAsync(taxonomy, token, 10, ct);

        result.Count.ShouldBe(3);
        result[0].Kind.ShouldBe(SuggestionKind.Region);
        result[1].Kind.ShouldBe(SuggestionKind.Employer,
            "dimension before free text: an employer sets ?employer=<org.nr>, the canonical key, "
            + "while Title is residual q.");
        result[2].Kind.ShouldBe(SuggestionKind.Title);
    }

    /// <summary>
    /// The pin for WHERE the budget is applied. Four employers match and one is personnummer-shaped;
    /// the excluded row must not consume a display slot, so the answer is 3 and not 2. This is the
    /// only fact that fails against the naive implementation that caps at the port call instead of at
    /// fill time — and the naive one is green under every other test in this file.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldNotSpendBudgetOnExcludedSoleProprietor()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = $"pnr{Guid.NewGuid():D}"[..14].Replace("-", "x", StringComparison.Ordinal);

        // The sole proprietorship sorts FIRST (most ads), so it is inside any cap the port applies.
        // Format-valid and personnummer-SHAPED (third digit 0), but not a real personnummer — this
        // literal lives in a tracked file forever.
        await SeedEmployerAdAsync("8501012384", $"{token} Enskild Firma", ct);
        await SeedEmployerAdAsync("8501012384", $"{token} Enskild Firma", ct);
        await SeedEmployerAdAsync("5566101001", $"{token} Alfa AB", ct);
        await SeedEmployerAdAsync("5566101002", $"{token} Beta AB", ct);
        await SeedEmployerAdAsync("5566101003", $"{token} Gamma AB", ct);

        var result = await RunAsync(TaxonomyReturning(), token, 10, ct);

        var employers = result.Where(x => x.Kind == SuggestionKind.Employer).ToList();

        employers.Count.ShouldBe(3,
            "the excluded sole proprietorship must not cost a display slot. If the budget were "
            + "applied at the port call instead of at fill time this would be 2 — and nothing else "
            + "in this file would notice.");

        employers.ShouldAllBe(x => x.OrganizationNumber != "8501012384");
        employers.ShouldAllBe(x => !x.IsProtectedIdentity);
        result.ShouldAllBe(x => x.Label != $"{token} Enskild Firma");
    }

}
