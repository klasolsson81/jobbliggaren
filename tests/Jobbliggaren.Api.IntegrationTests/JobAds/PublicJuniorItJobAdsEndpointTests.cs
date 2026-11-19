using System.Net;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The open junior IT list <c>GET /api/v1/public/job-ads/junior</c> (ADR 0186) on the real host, anonymously: the real
/// list, the real taxonomy and the shared database. Other classes' rows live in that database, so an ad is looked for in
/// a kommun no other class uses, and the exact set and order are asserted on a database of their own in
/// <c>JuniorItJobAdQueryReadTests</c>.
/// </summary>
[Collection("Api")]
public sealed class PublicJuniorItJobAdsEndpointTests(ApiFactory factory)
{
    private const string Route = "/api/v1/public/job-ads/junior";
    private const string DataIt = "DJh5_yyF_hEM";
    private const string Olofstrom = "1gEC_kvM_TXK";
    private const string BlekingeLan = "DQZd_uYs_oKb";

    // Shaped like a concept id, and in no taxonomy snapshot.
    private const string NoSuchKommun = "Zz00_zzz_zzz";

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = DateTimeOffset.UtcNow };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("PublishedAtDesc")]
    [InlineData("ExpiresAtAsc")]
    public async Task A_page_is_two_keys_rows_and_places_of_seven_member_rows_and_is_not_stored(string sortBy)
    {
        var ad = await ImportJuniorAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Route}?municipality={Olofstrom}&sortBy={sortBy}&page=1", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["rows", "places"], ignoreOrder: true);

        var rows = body.RootElement.GetProperty("rows");
        rows.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["items", "totalCount", "page", "pageSize", "totalPages"], ignoreOrder: true);
        rows.GetProperty("pageSize").GetInt32().ShouldBe(PublicJuniorItJobAdLimits.PageSize);
        var items = rows.GetProperty("items").EnumerateArray().ToList();
        items.ShouldNotBeEmpty();
        foreach (var item in items)
        {
            item.EnumerateObject().Select(p => p.Name).ShouldBe(
                ["id", "title", "companyName", "municipalityName", "regionName", "publishedAt", "expiresAt"],
                ignoreOrder: true,
                "seven values and nothing else: no description, no link to Arbetsförmedlingen, no concept id");
        }
        var row = items.Single(i => i.GetProperty("id").GetString() == ad.ToString("D"));
        row.GetProperty("municipalityName").GetString().ShouldBe("Olofström");
        row.GetProperty("regionName").GetString().ShouldBe("Blekinge län");

        var places = body.RootElement.GetProperty("places").EnumerateArray().ToList();
        foreach (var place in places)
            place.EnumerateObject().Select(p => p.Name).ShouldBe(["municipalityConceptId", "name", "count"], ignoreOrder: true);
        var olofstrom = places.Single(p => p.GetProperty("municipalityConceptId").GetString() == Olofstrom);
        olofstrom.GetProperty("name").GetString().ShouldBe("Olofström");
        olofstrom.GetProperty("count").GetInt32().ShouldBe(items.Count);
    }

    [Fact]
    public async Task A_kommun_without_a_listed_junior_ad_is_an_empty_first_page()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Route}?municipality={NoSuchKommun}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var rows = body.RootElement.GetProperty("rows");
        rows.GetProperty("items").GetArrayLength().ShouldBe(0);
        rows.GetProperty("totalCount").GetInt32().ShouldBe(0);
        rows.GetProperty("page").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_page_past_the_last_is_a_404_and_is_not_stored()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Route}?municipality={NoSuchKommun}&page=2", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    // The validator's road, then the binder's.
    [Theory]
    [InlineData("?sortBy=Relevance")]
    [InlineData("?sortBy=PublishedAtAsc")]
    [InlineData("?sortBy=ExpiresAtDesc")]
    [InlineData("?sortBy=bogus")]
    [InlineData("?municipality=AvNB-uwa-6n6")]
    [InlineData("?municipality=G%C3%B6teborg")]
    [InlineData("?municipality=x%27%20OR%20%271%27%3D%271")]
    [InlineData("?municipality=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("?page=0")]
    [InlineData("?page=101")]
    [InlineData("?page=abc")]
    public async Task A_value_the_list_refuses_is_a_400_and_is_not_stored(string query)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Route}{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    private async Task<Guid> ImportJuniorAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = TestIds.ExternalId();
        var jobAd = JobAd.Import(
            title: "Junior utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en junior utvecklare till vårt team.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(occupationGroup: DataIt, municipality: Olofstrom, region: BlekingeLan),
            declaredContacts: [],
            publishedAt: _clock.UtcNow.AddMinutes(-5),
            expiresAt: _clock.UtcNow.AddDays(14),
            clock: _clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);
        return jobAd.Id.Value;
    }
}
