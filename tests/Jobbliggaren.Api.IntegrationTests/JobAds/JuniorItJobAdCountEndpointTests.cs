using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// ADR 0188 (#2265) — <c>GET /api/v1/job-ads/junior-it-count</c> on the real host, in the form of
/// <c>RemoteAdCountEndpointTests</c>: a signed-in read, uncacheable, that answers the open list's own total. Other
/// classes' ads live in the shared database, so the count is compared with <c>/junior</c>'s total read right after it,
/// never with a fixed number; the exact number is <c>JuniorItJobAdCountTests</c>' assertion, on a database of its own.
/// </summary>
[Collection("Api")]
public sealed class JuniorItJobAdCountEndpointTests(ApiFactory factory)
{
    private const string Route = "/api/v1/job-ads/junior-it-count";
    private const string OpenList = "/api/v1/public/job-ads/junior";
    private const string DataIt = "DJh5_yyF_hEM";
    private const string Stockholm = "AvNB_uwa_6n6";
    private const string StockholmsLan = "CifL_Rzy_Mku";

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = DateTimeOffset.UtcNow };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GET_junior_it_count_without_a_session_returns_401()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(Route, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_junior_it_count_answers_the_open_lists_total_uncacheable_and_nothing_else()
    {
        await ImportJuniorAsync();
        using var client = await SignedInClientAsync();
        using var anonymous = factory.CreateClient();

        var response = await client.GetAsync(Route, Ct);
        var openList = await anonymous.GetAsync(OpenList, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cacheControl = response.Headers.CacheControl.ShouldNotBeNull();
        cacheControl.Private.ShouldBeTrue();
        cacheControl.NoStore.ShouldBeTrue();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["count"], "a count, and nothing about any ad");
        var count = body.RootElement.GetProperty("count").GetInt32();
        count.ShouldBeGreaterThanOrEqualTo(1, "this test's own listed junior ad is counted");

        openList.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var list = JsonDocument.Parse(await openList.Content.ReadAsStringAsync(Ct));
        count.ShouldBe(list.RootElement.GetProperty("rows").GetProperty("totalCount").GetInt32());
    }

    // security-auditor (#2265): the limiter's OnRejected sets Retry-After and nothing else, so the 429 is asserted to
    // carry no count, and its caching headers are not asserted at all.
    [Fact]
    public async Task GET_junior_it_count_past_the_bucket_is_a_429_with_Retry_After_and_no_count()
    {
        var hosted = factory.Services.GetRequiredService<IConfiguration>()
            .GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        var burst = new RateLimitingOptions().JobAdJuniorItCount.PermitLimit;
        hosted.JobAdJuniorItCount.PermitLimit.ShouldBe(burst, "the shared host must run the production bucket");
        // A user of its own: the bucket is partitioned by the session's subject.
        using var client = await SignedInClientAsync();

        var answered = 0;
        HttpResponseMessage? rejected = null;
        for (var attempt = 0; attempt < burst * 2 && rejected is null; attempt++)
        {
            var response = await client.GetAsync(Route, Ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                answered++;
                response.Dispose();
            }
        }

        answered.ShouldBeGreaterThanOrEqualTo(burst);
        rejected.ShouldNotBeNull();
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        (await rejected.Content.ReadAsStringAsync(Ct)).ShouldNotContain("\"count\"", Case.Insensitive);
        rejected.Dispose();
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    // Imported as the ingestion stores it, then classified by the host's own sweep, as the Worker's tick would.
    private async Task ImportJuniorAsync()
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
            facets: TestFacets.From(occupationGroup: DataIt, municipality: Stockholm, region: StockholmsLan),
            declaredContacts: [],
            publishedAt: _clock.UtcNow.AddMinutes(-5),
            expiresAt: _clock.UtcNow.AddDays(14),
            clock: _clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);

        await scope.ServiceProvider.GetRequiredService<ClassifyJuniorJobAdsJob>().RunAsync(Ct);
    }
}
