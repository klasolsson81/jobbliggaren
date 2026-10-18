using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The public catalogue's pages <c>GET /api/v1/public/job-ads/catalogue</c> (ADR 0175) on the real host, anonymously.
/// The timer is taken out of the host so the test drives the sampler itself; the clock is the host's own. The shared
/// database holds other classes' rows, so an ad is looked up on the page the snapshot puts it on, and the exact set and
/// order are asserted on a database of their own in <c>PublicJobAdCatalogueReadTests</c>.
/// </summary>
[Collection("Api")]
public sealed class PublicJobAdCatalogueEndpointTests(ApiFactory factory)
{
    private const string Route = "/api/v1/public/job-ads/catalogue";
    private static readonly Regex LowerCaseGuid = new("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = DateTimeOffset.UtcNow };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> WithoutTheTimer(IListedJobAdSnapshotReader? reader = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(ListedJobAdSnapshotService)));
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(_clock);
            if (reader is not null)
            {
                services.RemoveAll<IListedJobAdSnapshotReader>();
                services.AddSingleton(reader);
            }
        }));

    [Fact]
    public async Task Before_any_read_a_page_is_a_503_with_Retry_After_and_no_store()
    {
        await using var host = WithoutTheTimer();
        using var client = host.CreateClient();

        var response = await client.GetAsync($"{Route}?page=1", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(60));
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task After_a_read_a_page_is_five_keys_of_six_member_rows_and_holds_only_listed_ads()
    {
        await using var host = WithoutTheTimer();
        using var client = host.CreateClient();
        var listed = await ImportAsync(host);
        var archived = await ImportAsync(host, ad => ad.Archive(_clock));
        var erased = await ImportAsync(host, ad => ad.Erase(_clock));
        var neverImported = await CreateManualAsync(host);
        await SampleAsync(host);

        var order = Snapshot(host).CatalogueOrder;
        order.ShouldNotContain(archived);
        order.ShouldNotContain(erased);
        order.ShouldNotContain(neverImported);
        var response = await client.GetAsync($"{Route}?page={PageOf(host, listed)}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["items", "totalCount", "page", "pageSize", "totalPages"], ignoreOrder: true);
        body.RootElement.GetProperty("pageSize").GetInt32().ShouldBe(PublicJobAdCatalogueLimits.PageSize);
        var rows = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        rows.ShouldNotBeEmpty();
        foreach (var row in rows)
        {
            row.EnumerateObject().Select(p => p.Name).ShouldBe(
                ["id", "title", "companyName", "municipalityName", "regionName", "publishedAt"], ignoreOrder: true,
                "six values and nothing else: no description, no link to Arbetsförmedlingen, no deadline");
            LowerCaseGuid.IsMatch(row.GetProperty("id").GetString()!).ShouldBeTrue();
        }
        rows.Select(r => r.GetProperty("id").GetString()).ShouldContain(listed.ToString("D"));
    }

    [Fact]
    public async Task An_ad_erased_after_the_read_is_absent_from_its_page_at_once()
    {
        await using var host = WithoutTheTimer();
        using var client = host.CreateClient();
        var ad = await ImportAsync(host);
        await SampleAsync(host);
        var page = PageOf(host, ad);

        await EraseAsync(host, ad);
        var response = await client.GetAsync($"{Route}?page={page}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetString())
            .ShouldNotContain(ad.ToString("D"));
    }

    [Fact]
    public async Task A_page_past_the_last_is_a_404_and_no_store()
    {
        await using var host = WithoutTheTimer();
        using var client = host.CreateClient();
        await SampleAsync(host);
        var lastPage = Math.Max(1, (Snapshot(host).Count + PublicJobAdCatalogueLimits.PageSize - 1) / PublicJobAdCatalogueLimits.PageSize);

        var response = await client.GetAsync($"{Route}?page={lastPage + 1}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task A_snapshot_older_than_its_bound_is_a_503()
    {
        await using var host = WithoutTheTimer();
        using var client = host.CreateClient();
        await SampleAsync(host);
        _clock.UtcNow += ListedJobAdSnapshotLimits.MaxAge + TimeSpan.FromSeconds(1);

        var response = await client.GetAsync($"{Route}?page=1", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_empty_corpus_answers_page_one_with_exactly_this_body_and_has_no_page_two()
    {
        // A measured zero: a read that found no listed ad publishes an empty snapshot (ADR 0174 D1).
        await using var host = WithoutTheTimer(new FixedReader(ListedJobAdSnapshot.Create(_clock.UtcNow, [])));
        using var client = host.CreateClient();

        var one = await client.GetAsync($"{Route}?page=1", Ct);
        var two = await client.GetAsync($"{Route}?page=2", Ct);

        one.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await one.Content.ReadAsStringAsync(Ct))
            .ShouldBe("""{"items":[],"totalCount":0,"page":1,"pageSize":20,"totalPages":0}""");
        two.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // The validator's road (a page outside 1..MaxPage) and the binder's road (no page, or one that does not parse).
    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=4001")]
    [InlineData("?page=abc")]
    [InlineData("")]
    public async Task A_page_outside_the_cap_or_one_that_does_not_parse_is_a_400_and_no_store(string query)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Route}{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    private static ListedJobAdSnapshot Snapshot(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<IListedJobAdSnapshotReader>().Current.ShouldNotBeNull();

    private static int PageOf(WebApplicationFactory<Program> host, Guid id)
    {
        var index = Snapshot(host).CatalogueOrder.ToList().IndexOf(id);
        index.ShouldBeGreaterThanOrEqualTo(0, "the ad is listed, so the snapshot holds it");
        return index / PublicJobAdCatalogueLimits.PageSize + 1;
    }

    private static Task SampleAsync(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<IListedJobAdSnapshotSampler>().SampleAsync(Ct);

    private async Task<Guid> ImportAsync(WebApplicationFactory<Program> host, Func<JobAd, Result>? then = null)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = TestIds.ExternalId();
        var jobAd = JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en backend-utvecklare till vårt team.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: _clock.UtcNow.AddDays(-3),
            expiresAt: null,
            clock: _clock,
            extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);

        // A lifecycle change goes through the aggregate's own transition, never a column write (#843).
        if (then is not null)
        {
            then(jobAd).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        return jobAd.Id.Value;
    }

    /// <summary>Erases an ad the way <c>EraseRecruiterAdsCommand</c> does: loaded, erased through the aggregate, saved.</summary>
    private async Task EraseAsync(WebApplicationFactory<Program> host, Guid id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.FindAsync([new JobAdId(id)], Ct);
        ad.ShouldNotBeNull().Erase(_clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>What <c>POST /api/v1/job-ads</c> stores: <c>Source = Platsbanken</c>, and no import provenance.</summary>
    private async Task<Guid> CreateManualAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobAd = JobAd.Create(
            title: "Backend-utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en backend-utvecklare till vårt team.",
            url: "https://example.com/annons",
            source: JobSource.Platsbanken,
            publishedAt: _clock.UtcNow.AddDays(-3),
            expiresAt: null,
            clock: _clock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);
        return jobAd.Id.Value;
    }

    private sealed class FixedReader(ListedJobAdSnapshot snapshot) : IListedJobAdSnapshotReader
    {
        public ListedJobAdSnapshot? Current { get; } = snapshot;
    }
}
