using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2199 — the /jobb occupation-name filter against real Postgres, through the host's search port and
/// the committed occupation-name lineage. Every id is a real JobTech concept, and each premise about
/// the lineage is asserted before the search it explains.
/// <para>
/// Every seed is an imported ad whose facets are read from its payload, as
/// <c>PlatsbankenJobSource.MapFacets</c> reads them: it copies <c>occupation.concept_id</c>
/// verbatim, deprecated ids included (23.8% of active ads, measured 2026-10-24), and an ad's group is
/// its own name's group (no name changed group between taxonomy v30 and v31). Each test isolates its
/// ads by a run-unique employment type, which every search below also filters on.
/// </para>
/// </summary>
[Collection("Api")]
public class ListJobAdsOccupationNameFilterTests(ApiFactory factory)
{
    private const string ProjektledareIt = "z5AM_ayf_WcL";
    private const string Systemutvecklare = "fg7B_yov_smw";
    private const string MjukvaruGroup = "DJh5_yyF_hEM";
    private const string Grundlarare46V30 = "S9LC_4m4_yza";
    private const string Grundlarare46V31 = "743P_CSD_tF8";
    private const string LarareGrundskolan46 = "Nzmh_iTb_z7h";
    private const string GrundskollarareGroup = "oQUQ_D11_HPx";
    private const string Fiskeriintendent = "1d9y_tgk_awJ";
    private const string Fiskerikonsulent = "BLeD_2gg_F6H";
    private const string Fiskerikontrollant = "whBX_Ndk_opm";
    private const string FiskeriintendentGroup = "XurM_DVs_ggn";
    private const string Specialistlakare = "oHyy_M2j_HUP";
    private const string SpecialistlakareGroup = "nu7c_QuB_KVQ";
    private const string Allmanmedicin = "aCoU_U6i_fza";
    private const string Psykiatri = "TG6L_znT_mbR";

    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task ProjektledareIt_FindsItsOwnAds_NotTheWholeGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = RunKey();
        await SeedAsync("PL 1", ProjektledareIt, MjukvaruGroup, run, ct);
        await SeedAsync("PL 2", ProjektledareIt, MjukvaruGroup, run, ct);
        await SeedAsync("Utvecklare", Systemutvecklare, MjukvaruGroup, run, ct);

        (await TitlesAsync(run, names: [ProjektledareIt], ct: ct)).ShouldBe(["PL 1", "PL 2"], ignoreOrder: true);
        (await TitlesAsync(run, groups: [MjukvaruGroup], ct: ct)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task AGroupAndAName_AreAUnionInsideYrke()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = RunKey();
        await SeedAsync("PL", ProjektledareIt, MjukvaruGroup, run, ct);
        await SeedAsync("Utvecklare", Systemutvecklare, MjukvaruGroup, run, ct);
        await SeedAsync("Lärare", Grundlarare46V30, GrundskollarareGroup, run, ct);

        (await TitlesAsync(run, groups: [GrundskollarareGroup], names: [ProjektledareIt], ct: ct))
            .ShouldBe(["PL", "Lärare"], ignoreOrder: true);
    }

    // Grundlärare 4-6 was replaced in v31 by Grundlärare 4–6, which also replaced Lärare i
    // grundskolan, årskurs 4-6. A pick from either end finds all three.
    [Fact]
    public async Task AReplacedName_IsOneNameWithItsReplacements_FromEitherEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        Lineage().TargetOf(Grundlarare46V30).ShouldBe(Grundlarare46V31, "premise: S9LC was replaced by 743P in v31");
        var run = RunKey();
        await SeedAsync("v30", Grundlarare46V30, GrundskollarareGroup, run, ct);
        await SeedAsync("v31", Grundlarare46V31, GrundskollarareGroup, run, ct);
        await SeedAsync("äldre", LarareGrundskolan46, GrundskollarareGroup, run, ct);

        (await TitlesAsync(run, names: [Grundlarare46V30], ct: ct)).Count.ShouldBe(3);
        (await TitlesAsync(run, names: [Grundlarare46V31], ct: ct)).Count.ShouldBe(3);
    }

    // Fiskeriintendent was replaced by a name in its own group and one in another. Its ad, carrying
    // its own group, belongs to the successor in that group only.
    [Fact]
    public async Task AMultiReplacedName_AttachesToTheSuccessorInTheAdsGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        Lineage().Resolve(Fiskeriintendent, FiskeriintendentGroup).ShouldBe(
            Fiskerikonsulent, "premise: Fiskerikonsulent is the one successor in Fiskeriintendent's group");
        var run = RunKey();
        await SeedAsync("Intendent", Fiskeriintendent, FiskeriintendentGroup, run, ct);

        (await TitlesAsync(run, names: [Fiskerikonsulent], ct: ct)).ShouldBe(["Intendent"]);
        (await TitlesAsync(run, names: [Fiskerikontrollant], ct: ct)).ShouldBeEmpty();
    }

    // The generic Specialistläkare was replaced by 18 specialisms in its own group: no specialism
    // claims its ad, and the group still finds it.
    [Fact]
    public async Task AnAmbiguousMulti_IsReachableOnlyThroughItsGroup()
    {
        var ct = TestContext.Current.CancellationToken;
        Lineage().Resolve(Specialistlakare, SpecialistlakareGroup).ShouldBeNull("premise: all 18 successors share the group");
        var run = RunKey();
        await SeedAsync("Specialist", Specialistlakare, SpecialistlakareGroup, run, ct);

        (await TitlesAsync(run, names: [Allmanmedicin, Psykiatri], ct: ct)).ShouldBeEmpty();
        (await TitlesAsync(run, groups: [SpecialistlakareGroup], ct: ct)).ShouldBe(["Specialist"]);
    }

    // A taxonomy release after the artefact's brings names it does not know; the ingest stores them
    // verbatim, and a pick of one finds exactly its own ads.
    [Fact]
    public async Task ANameTheLineageDoesNotKnow_MatchesOnlyItself()
    {
        var ct = TestContext.Current.CancellationToken;
        var unknown = $"new{Guid.NewGuid():N}"[..12];
        var run = RunKey();
        await SeedAsync("Ny", unknown, MjukvaruGroup, run, ct);
        await SeedAsync("PL", ProjektledareIt, MjukvaruGroup, run, ct);

        (await TitlesAsync(run, names: [unknown], ct: ct)).ShouldBe(["Ny"]);
    }

    // Every variant's count is the total its pick lists, and a ticked name does not narrow the
    // count of a group or of another name: Yrke is one dimension, excluded whole.
    [Fact]
    public async Task OccupationNameCounts_EqualEachPicksListTotal()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = RunKey();
        await SeedAsync("PL", ProjektledareIt, MjukvaruGroup, run, ct);
        await SeedAsync("Utvecklare", Systemutvecklare, MjukvaruGroup, run, ct);
        await SeedAsync("v30", Grundlarare46V30, GrundskollarareGroup, run, ct);
        await SeedAsync("v31", Grundlarare46V31, GrundskollarareGroup, run, ct);
        await SeedAsync("äldre", LarareGrundskolan46, GrundskollarareGroup, run, ct);
        await SeedAsync("Intendent", Fiskeriintendent, FiskeriintendentGroup, run, ct);
        await SeedAsync("Specialist", Specialistlakare, SpecialistlakareGroup, run, ct);

        using var scope = _factory.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IJobAdSearchQuery>();
        var counts = await search.FacetCountsAsync(Filter(run, names: [ProjektledareIt]), FacetDimension.OccupationName, ct);

        foreach (var (pick, count) in counts)
            (await TitlesAsync(run, names: [pick], ct: ct)).Count.ShouldBe(count, pick);
        counts.ShouldContainKeyAndValue(ProjektledareIt, 1);
        counts.ShouldContainKeyAndValue(Systemutvecklare, 1);
        counts.ShouldContainKeyAndValue(Grundlarare46V30, 3);
        counts.ShouldContainKeyAndValue(Grundlarare46V31, 3);
        counts.ShouldContainKeyAndValue(Fiskerikonsulent, 1);
        counts.ShouldNotContainKey(Fiskerikontrollant);
        counts.ShouldNotContainKey(LarareGrundskolan46, "a name current in no version gets no count");
        counts.ShouldNotContainKey(Allmanmedicin, "the generic Specialistläkare attaches to no specialism");

        var groupCounts = await search.FacetCountsAsync(Filter(run, names: [ProjektledareIt]), FacetDimension.OccupationGroup, ct);
        groupCounts.ShouldContainKeyAndValue(MjukvaruGroup, 2);
    }

    // The endpoint binds repeated ?occupationName= and ORs it with ?occupationGroup=.
    [Fact]
    public async Task GET_job_ads_BindsOccupationName()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = RunKey();
        await SeedAsync("PL", ProjektledareIt, MjukvaruGroup, run, ct);
        await SeedAsync("Utvecklare", Systemutvecklare, MjukvaruGroup, run, ct);
        await SeedAsync("Lärare", Grundlarare46V30, GrundskollarareGroup, run, ct);
        var client = await AuthenticatedClientAsync(ct);

        var response = await client.GetAsync(
            $"/api/v1/job-ads?employmentType={run}&occupationName={ProjektledareIt}"
            + $"&occupationGroup={GrundskollarareGroup}&pageSize=100", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("title").GetString())
            .ShouldBe(["PL", "Lärare"], ignoreOrder: true);
    }

    [Fact]
    public async Task GET_facet_counts_AnswersTheOccupationNameDimension()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = RunKey();
        await SeedAsync("v30", Grundlarare46V30, GrundskollarareGroup, run, ct);
        await SeedAsync("äldre", LarareGrundskolan46, GrundskollarareGroup, run, ct);
        var client = await AuthenticatedClientAsync(ct);

        var response = await client.GetAsync(
            $"/api/v1/job-ads/facet-counts?dimension=OccupationName&employmentType={run}"
            + $"&occupationName={ProjektledareIt}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var counts = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>(ct);
        counts.ShouldNotBeNull();
        counts.ShouldContainKeyAndValue(Grundlarare46V30, 2);
        counts.ShouldContainKeyAndValue(Grundlarare46V31, 2);
    }

    [Fact]
    public async Task GET_job_ads_RejectsAnOccupationNameOutsideTheConceptIdGrammar()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);

        var response = await client.GetAsync("/api/v1/job-ads?occupationName=dot.notation", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> AuthenticatedClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private OccupationNameLineage Lineage() =>
        _factory.Services.GetRequiredService<IOccupationNameLineageProvider>().Lineage;

    private static string RunKey() => $"emp{Guid.NewGuid():N}"[..16];

    private async Task<List<string>> TitlesAsync(
        string run,
        IReadOnlyList<string>? groups = null,
        IReadOnlyList<string>? names = null,
        CancellationToken ct = default)
    {
        using var scope = _factory.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IJobAdSearchQuery>();
        var filter = Filter(run, groups, names);

        var page = await search.SearchAsync(
            new JobAdSearchCriteria(filter, JobAdSortBy.PublishedAtDesc, Page: 1, PageSize: 100), ct);
        var count = await search.CountAsync(filter, ct);
        count.ShouldBe(page.TotalCount, "the count path and the list path compose the same filter");
        return [.. page.Items.Select(i => i.Title)];
    }

    private static JobAdFilterCriteria Filter(
        string run, IReadOnlyList<string>? groups = null, IReadOnlyList<string>? names = null) => new(
        OccupationGroup: groups ?? [],
        OccupationName: names ?? [],
        Municipality: [],
        Region: [],
        EmploymentType: [run],
        WorktimeExtent: [],
        Employer: [],
        Remote: false,
        Q: null);

    private async Task SeedAsync(string title, string occupationName, string group, string run, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var externalId = $"ext-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"occupation\":{{\"concept_id\":\"{occupationName}\"}}," +
            $"\"occupation_group\":{{\"concept_id\":\"{group}\"}},\"employment_type\":{{\"concept_id\":\"{run}\"}}}}";

        db.JobAds.Add(JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value);
        await db.SaveChangesAsync(ct);
    }
}
