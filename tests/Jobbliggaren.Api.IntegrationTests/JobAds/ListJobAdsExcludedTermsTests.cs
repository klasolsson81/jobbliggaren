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
    private const string StockholmKommunConceptId = "AvNB_uwa_6n6";     // taxonomy-snapshot.json, kommun "Stockholm"
    private const string StockholmsLanConceptId = "CifL_Rzy_Mku";       // its län
    private const string GoteborgKommunConceptId = "PVZL_BQT_XtL";      // taxonomy-snapshot.json, kommun "Göteborg"
    private const string VastraGotalandsLanConceptId = "zdoY_6u5_Krt";  // its län
    private const string VikariatConceptId = "gro4_cWF_6D7";            // klass2-taxonomy.json, EmploymentType "Vikariat"
    private const string BehovsanstallningConceptId = "1paU_aCR_nGn";   // klass2-taxonomy.json, EmploymentType "Behovsanställning"
    private const string YrkeslarareConceptId = "sETD_6ca_PbW";         // taxonomy-snapshot.json, yrke "Yrkeslärare", grupp 2x2V_UeL_6ke
    private const string SkogslarareConceptId = "g7NJ_ZHN_MPB";         // taxonomy-snapshot.json, yrke in the same grupp
    private const string GymnasielarareGroupConceptId = "4KhP_FxL_uZ5"; // taxonomy-snapshot.json, yrkesgrupp "Gymnasielärare"

    private readonly ApiFactory _factory = factory;

    private static string RunToken() => $"tok{Guid.NewGuid():N}"[..16];

    private async Task<Guid> SeedAsync(
        string title,
        string description,
        CancellationToken ct,
        string company = "Test Company AB",
        string? worktimeExtent = null,
        string? occupationGroup = null,
        string? ssyk = null,
        string? municipality = null,
        string? region = null,
        string? employmentType = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var externalId = $"ext-{Guid.NewGuid():N}";

        static string Concept(string? id) => id is null ? "null" : $"{{\"concept_id\":\"{id}\"}}";
        static string Str(string? value) => value is null ? "null" : $"\"{value}\"";
        var workplace = municipality is null && region is null
            ? "null"
            : $"{{\"municipality_concept_id\":{Str(municipality)},\"region_concept_id\":{Str(region)}}}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"working_hours_type\":{Concept(worktimeExtent)}," +
            $"\"employment_type\":{Concept(employmentType)}," +
            $"\"occupation_group\":{Concept(occupationGroup)}," +
            $"\"occupation\":{Concept(ssyk)}," +
            $"\"workplace_address\":{workplace}}}";

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
            clock: clock, declaredContacts: [], applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;

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
        var likeAt = exclusion.IndexOf(" LIKE ", StringComparison.OrdinalIgnoreCase);
        likeAt.ShouldBeGreaterThanOrEqualTo(0, $"the exclusion carries no substring arm. SQL:\n{sql}");
        exclusion.IndexOf("@@", StringComparison.Ordinal).ShouldBeGreaterThan(likeAt, $"SQL:\n{sql}");
        exclusion.ShouldContain("plainto_tsquery", Case.Insensitive);
        exclusion.ShouldNotContain("websearch_to_tsquery", Case.Insensitive);
        sql[..sql.IndexOf("NOT EXISTS", StringComparison.OrdinalIgnoreCase)]
            .ShouldContain("websearch_to_tsquery", Case.Insensitive);

        // The escaped pattern needs '\' as its escape; EF's two-argument Like would emit ESCAPE ''.
        sql.ShouldContain("ESCAPE '\\'", Case.Insensitive);
        sql.ShouldNotContain("ESCAPE ''", Case.Insensitive);

        // A word without an indexable trigram gets no substring arm (security-auditor round 1).
        var noTrigram = await resolver.ResolveAsync(Filter("lärare -c++"), ct);
        var noTrigramSql = JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), noTrigram).ToQueryString();
        noTrigramSql[noTrigramSql.IndexOf("NOT EXISTS", StringComparison.OrdinalIgnoreCase)..]
            .ShouldNotContain(" LIKE ", Case.Insensitive);

        var positiveNoTrigram = await resolver.ResolveAsync(Filter("c++"), ct);
        JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), positiveNoTrigram).ToQueryString()
            .ShouldNotContain(" LIKE ", Case.Insensitive);

        var minusFree = await resolver.ResolveAsync(Filter("lärare deltid"), ct);
        JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), minusFree).ToQueryString()
            .ShouldNotContain("EXISTS", Case.Insensitive);
    }

    // The synonym arm too precedes the full-text arm (ADR 0167 D7), here through the host's own
    // configuration, which expands "systemutvecklare".
    [Fact]
    public async Task TheExclusionsSsykArm_PrecedesItsFullTextArm()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = SearchTextResolvers.For(scope, scope.ServiceProvider.GetRequiredService<IOccupationSynonymExpander>());

        var resolved = await resolver.ResolveAsync(Filter("-systemutvecklare"), ct);
        resolved.Text.Excluded.ShouldHaveSingleItem().Ssyks.ShouldNotBeEmpty("premise: the host's config expands the word");
        var sql = JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), resolved).ToQueryString();
        var exclusion = sql[sql.IndexOf("NOT EXISTS", StringComparison.OrdinalIgnoreCase)..];

        var ssykAt = exclusion.IndexOf("ssyk_concept_id", StringComparison.OrdinalIgnoreCase);
        ssykAt.ShouldBeGreaterThanOrEqualTo(0, $"SQL:\n{sql}");
        exclusion.IndexOf("@@", StringComparison.Ordinal).ShouldBeGreaterThan(ssykAt, $"SQL:\n{sql}");
    }

    // Every label column the arm reads, each against a row that is non-null and does not match.
    [Fact]
    public async Task AMinusWord_HidesByEachLabelColumn_AndKeepsANonMatchingValue()
    {
        var ct = TestContext.Current.CancellationToken;
        var token = RunToken();

        var survivor = await SeedAsync($"{token} Kock", "Ingen mer text.", ct,
            municipality: GoteborgKommunConceptId, region: VastraGotalandsLanConceptId,
            employmentType: BehovsanstallningConceptId, worktimeExtent: HeltidConceptId,
            occupationGroup: PedagogikGroupConceptId, ssyk: SkogslarareConceptId);
        var byKommun = await SeedAsync($"{token} Kock", "Ingen mer text.", ct,
            municipality: StockholmKommunConceptId, region: StockholmsLanConceptId);
        var byAnstallningsform = await SeedAsync($"{token} Kock", "Ingen mer text.", ct,
            employmentType: VikariatConceptId);
        // Only the yrke is named "Yrkeslärare"; its grupp is "Lärare i yrkesämnen".
        var byYrke = await SeedAsync($"{token} Kock", "Ingen mer text.", ct,
            occupationGroup: PedagogikGroupConceptId, ssyk: YrkeslarareConceptId);
        var byYrkesgrupp = await SeedAsync($"{token} Kock", "Ingen mer text.", ct,
            occupationGroup: GymnasielarareGroupConceptId);

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var all = await IdsAsync(sut, Filter(token), ct);
        all.ShouldBe([survivor, byKommun, byAnstallningsform, byYrke, byYrkesgrupp], ignoreOrder: true);

        (await IdsAsync(sut, Filter($"{token} -stockholm"), ct)).ShouldBe(all.Except([byKommun]), ignoreOrder: true);
        (await IdsAsync(sut, Filter($"{token} -vikariat"), ct)).ShouldBe(all.Except([byAnstallningsform]), ignoreOrder: true);
        (await IdsAsync(sut, Filter($"{token} -yrkeslärare"), ct)).ShouldBe(all.Except([byYrke]), ignoreOrder: true);
        (await IdsAsync(sut, Filter($"{token} -gymnasielärare"), ct)).ShouldBe(all.Except([byYrkesgrupp]), ignoreOrder: true);
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
