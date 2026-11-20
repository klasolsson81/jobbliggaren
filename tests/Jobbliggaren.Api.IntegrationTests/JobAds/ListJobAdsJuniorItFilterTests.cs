using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.JobAds.Junior;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2261, #2264 — the junior IT list as a /jobb filter, through the signed-in endpoints on real Postgres. Every row is
/// imported through <c>JobAd.Import</c>, as the ingestion stores it, and classified by the host's own
/// <see cref="ClassifyJuniorJobAdsJob"/> under the shipped rules, as the Worker's two-minute tick would (ADR 0187).
/// Each test isolates its rows in the shared database by a token in the title and the same token as <c>q</c>.
/// </summary>
[Collection("Api")]
public sealed class ListJobAdsJuniorItFilterTests(ApiFactory factory)
{
    private const string DataIt = "DJh5_yyF_hEM";
    private const string OtherDataIt = "hmaC_cfi_UKg";
    private const string NotDataIt = "2kLc_pto_DpV";
    private const string Stockholm = "AvNB_uwa_6n6";
    private const string StockholmsLan = "CifL_Rzy_Mku";
    private const string Goteborg = "PVZL_BQT_XtL";
    private const string VastraGotalandsLan = "zdoY_6u5_Krt";

    // The shipped hand reading's ids, each imported by one test only: (source, external id) is unique in the shared
    // database.
    private static readonly string[] ReadingIds =
        [.. JuniorRuleSetLoader.LoadDataIt().Reading.JuniorExternalIds.Order(StringComparer.Ordinal)];

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = DateTimeOffset.UtcNow };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewToken() => "jt" + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task Junior_ListsExactlyTheDataItAdsTheHeadlineOrTheCuratedListAdmits()
    {
        var token = NewToken();
        var curatedId = ReadingIds[0];
        var titled = await ImportAsync($"Junior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        var curated = await ImportAsync($"Systemutvecklare {token}", OtherDataIt, Goteborg, VastraGotalandsLan,
            externalId: curatedId);
        var senior = await ImportAsync($"Senior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        var outsideField = await ImportAsync($"Junior ekonomiassistent {token}", NotDataIt, Stockholm, StockholmsLan);
        using var client = await SignedInClientAsync();

        var junior = await IdsAsync(client, $"/api/v1/job-ads?q={token}&junior=true&pageSize=50");
        var all = await IdsAsync(client, $"/api/v1/job-ads?q={token}&pageSize=50");

        junior.ShouldBe([titled, curated], ignoreOrder: true);
        // The counterfactual: without the flag the same search lists all four, so the two left out above are the
        // junior clause's.
        all.ShouldBe([titled, curated, senior, outsideField], ignoreOrder: true);
    }

    [Fact]
    public async Task Junior_IsAndedWithThePlaceFilter()
    {
        var token = NewToken();
        var inStockholm = await ImportAsync($"Junior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        await ImportAsync($"Junior utvecklare {token}", DataIt, Goteborg, VastraGotalandsLan);
        await ImportAsync($"Senior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        using var client = await SignedInClientAsync();

        var ids = await IdsAsync(client, $"/api/v1/job-ads?q={token}&municipality={Stockholm}&junior=true");

        ids.ShouldBe([inStockholm]);
    }

    [Fact]
    public async Task Junior_ComposesWithTheSavedStatusFilter()
    {
        var token = NewToken();
        var savedJunior = await ImportAsync($"Junior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        var savedSenior = await ImportAsync($"Senior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        await ImportAsync($"Junior utvecklare {token}", DataIt, Goteborg, VastraGotalandsLan);
        using var client = await SignedInClientAsync();
        (await client.PostAsync($"/api/v1/me/saved-job-ads/{savedJunior}", null, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        (await client.PostAsync($"/api/v1/me/saved-job-ads/{savedSenior}", null, Ct)).IsSuccessStatusCode.ShouldBeTrue();

        var ids = await IdsAsync(client, $"/api/v1/job-ads?q={token}&savedOnly=true&junior=true");

        ids.ShouldBe([savedJunior]);
    }

    [Fact]
    public async Task FacetCounts_CountTheJuniorList()
    {
        var token = NewToken();
        await ImportAsync($"Junior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        await ImportAsync($"Trainee mjukvara {token}", OtherDataIt, Stockholm, StockholmsLan);
        await ImportAsync($"Senior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        await ImportAsync($"Junior utvecklare {token}", DataIt, Goteborg, VastraGotalandsLan);
        using var client = await SignedInClientAsync();

        var junior = await client.GetFromJsonAsync<Dictionary<string, int>>(
            $"/api/v1/job-ads/facet-counts?dimension=Municipality&q={token}&junior=true", Ct);
        var all = await client.GetFromJsonAsync<Dictionary<string, int>>(
            $"/api/v1/job-ads/facet-counts?dimension=Municipality&q={token}", Ct);

        junior.ShouldNotBeNull()[Stockholm].ShouldBe(2);
        junior[Goteborg].ShouldBe(1);
        all.ShouldNotBeNull()[Stockholm].ShouldBe(3);
    }

    // security-auditor, #2261 plan read, Minor 1: the harm a stored junior search would do is a Bump() of the
    // broader row it hashes like, so the existing row is the assertion, not only the absence of a new one.
    [Fact]
    public async Task ACommittedJuniorSearch_IsNotStored_AndLeavesTheBroaderRowUntouched()
    {
        var token = NewToken();
        await ImportAsync($"Junior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        using var client = await SignedInClientAsync();
        (await client.GetAsync($"/api/v1/job-ads?q={token}&commit=true", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var before = await StoredSearchesAsync(client);
        before.Count.ShouldBe(1);

        var response = await client.GetAsync($"/api/v1/job-ads?q={token}&junior=true&commit=true", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("isStorableAsRecentSearch").GetBoolean().ShouldBeFalse();
        var after = await StoredSearchesAsync(client);
        after.ShouldBe(before);
    }

    // #2264 — the /jobb ↔ /junior parity oracle. Over every arm the stored membership has, the /jobb junior filter
    // admits exactly the ads the open list lists: a rule's Junior, the hand reading's, no signal, an Unclear in the
    // queue, an Unclear an administrator decided Junior, a rule's Junior an administrator vetoed, and an ad outside
    // the field. Every ad is listed, so /jobb's own lifecycle gate (Active) and the list's (IsListed) agree on them.
    [Fact]
    public async Task Junior_AdmitsExactlyTheAdsTheOpenJuniorListLists()
    {
        var token = NewToken();
        var ruleJunior = await ImportAsync($"Junior utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        var readingJunior = await ImportAsync($"Systemutvecklare {token}", OtherDataIt, Stockholm, StockholmsLan,
            externalId: ReadingIds[1]);
        var noSignal = await ImportAsync($"Systemutvecklare {token}", DataIt, Stockholm, StockholmsLan);
        var queued = await ImportAsync($"Student utvecklare {token}", DataIt, Stockholm, StockholmsLan);
        var decidedJunior = await ImportAsync($"Student testare {token}", DataIt, Stockholm, StockholmsLan);
        var vetoed = await ImportAsync($"Junior testare {token}", DataIt, Stockholm, StockholmsLan);
        var outsideField = await ImportAsync($"Junior ekonomiassistent {token}", NotDataIt, Stockholm, StockholmsLan);
        var (admin, _, _) = await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        await DecideAsync(admin, decidedJunior, "Junior");
        await DecideAsync(admin, vetoed, "NotJunior");
        using var client = await SignedInClientAsync();

        var jobb = await IdsAsync(client, $"/api/v1/job-ads?q={token}&junior=true&pageSize=50");
        var openList = await OpenJuniorListIdsAsync(Stockholm);

        jobb.ShouldBe([ruleJunior, readingJunior, decidedJunior], ignoreOrder: true);
        new[] { ruleJunior, readingJunior, noSignal, queued, decidedJunior, vetoed, outsideField }
            .Where(openList.Contains)
            .ShouldBe(jobb, ignoreOrder: true);
    }

    [Fact]
    public async Task TheClause_IsTheListsOwnPredicate_AfterTheActiveGate()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = SearchTextResolvers.For(scope, Substitute.For<IOccupationSynonymExpander>());
        var groups = JuniorMembership.GroupsOf(
            await scope.ServiceProvider.GetRequiredService<IJuniorFieldCatalog>().ResolveEnabledAsync(Ct));

        var resolved = await resolver.ResolveAsync(Criteria(juniorIt: true), Ct);
        var actual = JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), resolved).ToQueryString();

        var expected = db.JobAds.AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active)
            .Where(JuniorMembership.Of(db.JuniorAssessments, groups))
            .ToQueryString();
        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task WithTheFlagOff_TheFilterComposesNoJuniorStep()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resolver = SearchTextResolvers.For(scope, Substitute.For<IOccupationSynonymExpander>());

        var resolved = await resolver.ResolveAsync(Criteria(juniorIt: false), Ct);

        resolved.JuniorItMembership.ShouldBeNull();
        JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), resolved).ToQueryString()
            .ShouldBe(db.JobAds.AsNoTracking().Where(j => j.Status == JobAdStatus.Active).ToQueryString());
    }

    private static JobAdFilterCriteria Criteria(bool juniorIt) => new(
        OccupationGroup: [],
        OccupationName: [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [],
        Employer: [],
        Remote: false,
        JuniorIt: juniorIt,
        Q: null);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private static async Task DecideAsync(HttpClient admin, Guid id, string decision)
    {
        using var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/junior-job-ads/{id}/decision", new { decision }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // The open list's own port, read through the host's clock: every ad it lists in the kommun, not one page.
    private async Task<HashSet<Guid>> OpenJuniorListIdsAsync(string municipality)
    {
        using var scope = factory.Services.CreateScope();
        var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow;
        var rows = await scope.ServiceProvider.GetRequiredService<IJuniorItJobAdQuery>()
            .ReadPageAsync(now, municipality, JobAdSortBy.PublishedAtDesc, skip: 0, take: 10_000, Ct);
        return rows.Select(r => r.Id).ToHashSet();
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => Guid.Parse(item.GetProperty("id").GetString()!))
            .ToList();
    }

    // The user's rows, found through their own list endpoint, with the fields a Bump() writes read from the table.
    private async Task<List<string>> StoredSearchesAsync(HttpClient client)
    {
        var items = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/recent-searches", Ct);
        var ids = items.EnumerateArray().Select(row => Guid.Parse(row.GetProperty("id").GetString()!)).ToList();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.RecentJobSearches.AsNoTracking().ToListAsync(Ct);
        return rows
            .Where(r => ids.Contains(r.Id.Value))
            .OrderBy(r => r.Id.Value)
            .Select(r => $"{r.Id.Value}|{r.Q}|{r.LastViewedAt:O}|{r.LastSeenCount}")
            .ToList();
    }

    private async Task<Guid> ImportAsync(
        string title, string occupationGroup, string municipality, string region, string? externalId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = externalId ?? TestIds.ExternalId();
        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Annonstext.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{id}",
            external: ExternalReference.Create(JobSource.Platsbanken, id).Value,
            rawPayload: $"{{\"id\":\"{id}\"}}",
            facets: TestFacets.From(occupationGroup: occupationGroup, municipality: municipality, region: region),
            declaredContacts: [],
            publishedAt: _clock.UtcNow.AddMinutes(-5),
            expiresAt: _clock.UtcNow.AddDays(14),
            clock: _clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);

        // The Worker's sweep classifies it, as its two-minute tick would (ADR 0187).
        await scope.ServiceProvider.GetRequiredService<ClassifyJuniorJobAdsJob>().RunAsync(Ct);
        return jobAd.Id.Value;
    }
}
