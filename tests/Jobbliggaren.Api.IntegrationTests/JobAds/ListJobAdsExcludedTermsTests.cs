using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2143 (ADR 0167) — a minus-word hides every ad the same word would find as a plus-word, and
/// every ad whose taxonomy label it names exactly. Against real Postgres, through the shared search
/// port, so the full-text arm, the two substring arms, the synonym arm and the label arm all run as
/// production composes them.
/// <para>
/// Every seed is an imported ad (<see cref="JobAd.Import"/>, the ingest path), its facets read from
/// the same payload variables. Each test isolates its ads by a run-unique token in the title that the
/// positive part of the search requires, or by a run-unique occupation group.
/// </para>
/// </summary>
[Collection("Api")]
public class ListJobAdsExcludedTermsTests(ApiFactory factory)
{
    private const string DeltidConceptId = "947z_JGS_Uk2"; // klass2-taxonomy.json, WorktimeExtent "Deltid"
    private const string HeltidConceptId = "6YE1_gAC_R2G"; // klass2-taxonomy.json, WorktimeExtent "Heltid"
    private const string PedagogikGroupConceptId = "2x2V_UeL_6ke"; // a yrkesgrupp under yrkesområdet Pedagogik

    private readonly ApiFactory _factory = factory;

    private static string RunToken() => $"tok{Guid.NewGuid():N}"[..16];

    private async Task<Guid> SeedAsync(
        string title,
        string description,
        CancellationToken ct,
        string company = "Test Company AB",
        string? worktimeExtent = null,
        string? occupationGroup = null,
        string? ssyk = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var externalId = $"ext-{Guid.NewGuid():N}";

        static string Concept(string? id) => id is null ? "null" : $"{{\"concept_id\":\"{id}\"}}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"working_hours_type\":{Concept(worktimeExtent)}," +
            $"\"occupation_group\":{Concept(occupationGroup)}," +
            $"\"occupation\":{Concept(ssyk)}}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create(company).Value,
            description: description,
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    // The real expander over a test configuration: the adapter emits whatever its options map.
    private static JobAdSearchQuery CreateSut(IServiceScope scope, Dictionary<string, string[]>? synonyms = null)
    {
        var options = new SearchSynonymsOptions();
        foreach (var (term, ids) in synonyms ?? [])
            options.Occupations[term] = ids;

        return new JobAdSearchQuery(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            SearchTextResolvers.For(scope, new OccupationSynonymExpander(Options.Create(options))));
    }

    private static JobAdFilterCriteria Filter(string q, IReadOnlyList<string>? occupationGroup = null) =>
        new(
            OccupationGroup: occupationGroup ?? [],
            Municipality: [],
            Region: [],
            EmploymentType: [],
            WorktimeExtent: [],
            Employer: [],
            Remote: false,
            Q: q);

    private static async Task<HashSet<Guid>> IdsAsync(JobAdSearchQuery sut, JobAdFilterCriteria filter, CancellationToken ct)
    {
        var page = await sut.SearchAsync(
            new JobAdSearchCriteria(filter, JobAdSortBy.PublishedAtDesc, Page: 1, PageSize: 100), ct);
        page.TotalCount.ShouldBe(page.Items.Count, "the count path and the list path must compose the same WHERE");
        return page.Items.Select(i => i.Id).ToHashSet();
    }

    [Fact]
    public async Task AMinusWord_HidesExactlyWhatTheWordFinds_ByEveryArm_AndItsLabel()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = RunToken();
        var ssyk = $"ssyk_{token}";

        var clean = await SeedAsync($"{token} Kock", "Heltid i köket.", ct, worktimeExtent: HeltidConceptId);
        var inDescription = await SeedAsync($"{token} Kock", "Tjänsten är på deltid.", ct);
        var inTitleSubstring = await SeedAsync($"{token} Deltidstjänst kock", "Ingen mer text.", ct);
        var inCompany = await SeedAsync($"{token} Kock", "Ingen mer text.", ct, company: "Deltid Bemanning AB");
        var bySynonym = await SeedAsync($"{token} Kock", "Ingen mer text.", ct, ssyk: ssyk);
        var byLabel = await SeedAsync($"{token} Kock", "Ingen mer text.", ct, worktimeExtent: DeltidConceptId);
        var noTaxonomy = await SeedAsync($"{token} Kock", "Ingen mer text.", ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope, new() { ["deltid"] = [ssyk] });

        var all = await IdsAsync(sut, Filter(token), ct);
        var excluded = await IdsAsync(sut, Filter($"{token} -deltid"), ct);

        all.ShouldBe([clean, inDescription, inTitleSubstring, inCompany, bySynonym, byLabel, noTaxonomy], ignoreOrder: true);
        excluded.ShouldBe(
            all.Except([inDescription, inTitleSubstring, inCompany, bySynonym, byLabel]),
            ignoreOrder: true);
        // An ad with every taxonomy column NULL names nothing and survives the label arm.
        excluded.ShouldContain(noTaxonomy);
    }

    [Theory]
    [InlineData("-deltid")]
    [InlineData("−deltid")] // U+2212 minus sign
    [InlineData("–deltid")] // U+2013 en dash
    public async Task AQueryOfOnlyMinusWords_FiltersTheDimensionSet(string q)
    {
        var ct = TestContext.Current.CancellationToken;
        var group = $"grp_{RunToken()}";

        var kept = await SeedAsync("Kock", "Heltid i köket.", ct, occupationGroup: group);
        await SeedAsync("Kock", "Tjänsten är på deltid.", ct, occupationGroup: group);
        await SeedAsync("Kock", "Ingen mer text.", ct, occupationGroup: group, worktimeExtent: DeltidConceptId);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var ids = await IdsAsync(sut, Filter(q, occupationGroup: [group]), ct);

        ids.ShouldBe([kept]);
        (await sut.CountAsync(Filter(q, occupationGroup: [group]), ct)).ShouldBe(1);
    }

    [Fact]
    public async Task AMinusWordNamingAnOccupationField_HidesTheFieldsGroups()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = RunToken();

        var kept = await SeedAsync($"{token} Kock", "Ingen mer text.", ct);
        await SeedAsync($"{token} Lärare", "Ingen mer text.", ct, occupationGroup: PedagogikGroupConceptId);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        (await IdsAsync(sut, Filter($"{token} -pedagogik"), ct)).ShouldBe([kept]);
    }

    [Fact]
    public async Task AFacetCount_RespectsTheMinusWord()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = RunToken();

        await SeedAsync($"{token} Kock", "Heltid i köket.", ct, worktimeExtent: HeltidConceptId);
        await SeedAsync($"{token} Kock", "Ingen mer text.", ct, worktimeExtent: DeltidConceptId);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var counts = await sut.FacetCountsAsync(Filter($"{token} -deltid"), FacetDimension.WorktimeExtent, ct);

        counts.ShouldBe(new Dictionary<string, int> { [HeltidConceptId] = 1 });
    }

    [Fact]
    public async Task ALikeMetacharacter_MatchesLiterally()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = RunToken();

        var literal = await SeedAsync($"{token} 100% distans", "Ingen mer text.", ct);
        await SeedAsync($"{token} 1000 kronor", "Ingen mer text.", ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        // Unescaped, '%' was a wildcard and the second ad's title matched "%<token> 100%%".
        (await IdsAsync(sut, Filter($"{token} 100%"), ct)).ShouldBe([literal]);
    }

    // The SQL EF generates from production's own expression tree — the other half of
    // JobAdPlannerUsabilityOracleTests.QExclusion_TheAntiJoinsInnerSideIsServedByEveryArm, whose
    // hand-written mirror is only safe while this shape holds.
    [Fact]
    public async Task TheExclusion_IsANotExistsOverTheUnfilteredTable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = SearchTextResolvers.For(scope, new OccupationSynonymExpander(Options.Create(new SearchSynonymsOptions())));

        var excluding = await resolver.ResolveAsync(Filter("lärare -deltid"), ct);
        var sql = JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), excluding).ToQueryString();

        sql.ShouldContain("NOT EXISTS", Case.Insensitive);
        // The Active filter stays on the outer side; inside the EXISTS it would let the status index
        // serve the inner side and detoast search_vector per probe.
        CountOf(sql, "status = ").ShouldBe(1, $"Expected one status predicate. SQL:\n{sql}");
        // "deltid" names the omfattning Deltid exactly, and an ad without one survives.
        sql.ShouldContain("worktime_extent_concept_id IS NULL OR", Case.Insensitive);
        // The full-text arm comes after the substring arms, so a title match spares the detoast.
        var exclusion = sql[sql.IndexOf("NOT EXISTS", StringComparison.OrdinalIgnoreCase)..];
        exclusion.IndexOf("@@", StringComparison.Ordinal)
            .ShouldBeGreaterThan(exclusion.IndexOf(" LIKE ", StringComparison.OrdinalIgnoreCase));

        var minusFree = await resolver.ResolveAsync(Filter("lärare deltid"), ct);
        JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), minusFree).ToQueryString()
            .ShouldNotContain("EXISTS", Case.Insensitive);
    }

    // One cache behind both taxonomy ports: two instances would read the snapshot twice and could
    // disagree after a reseed.
    [Fact]
    public void TheLabelMatcher_IsTheReadModelsOwnInstance()
    {
        using var scope = _factory.Services.CreateScope();

        var matcher = scope.ServiceProvider.GetRequiredService<ITaxonomyLabelMatcher>();
        var readModel = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();

        ReferenceEquals(matcher, readModel).ShouldBeTrue();
    }

    private static int CountOf(string text, string word) =>
        text.Split(word, StringSplitOptions.None).Length - 1;

    [Fact]
    public async Task AMinusFreeSearch_IsUnchangedByTheGrammar()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = RunToken();

        var a = await SeedAsync($"{token} Kock", "Tjänsten är på deltid.", ct);
        var b = await SeedAsync($"{token} Kock", "Heltid.", ct);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        (await IdsAsync(sut, Filter($"{token} deltid"), ct)).ShouldBe([a]);
        (await IdsAsync(sut, Filter($"{token}, kock"), ct)).ShouldBe([a, b], ignoreOrder: true);
    }
}
