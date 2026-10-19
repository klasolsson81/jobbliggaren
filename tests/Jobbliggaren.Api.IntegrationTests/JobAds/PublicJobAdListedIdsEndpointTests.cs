using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.JobAds.Listing;
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
/// The job sitemap's parts <c>GET /api/v1/public/job-ads/listed-ids</c> (ADR 0174) on the real host, anonymously. The
/// timer is taken out of the host so the test drives the sampler itself; the clock is the host's own. The shared
/// database holds other classes' rows, so membership is asserted here and the exact set in
/// <c>ListedJobAdSnapshotSamplerTests</c>.
/// </summary>
[Collection("Api")]
public sealed class PublicJobAdListedIdsEndpointTests(ApiFactory factory)
{
    private const string Route = "/api/v1/public/job-ads/listed-ids";
    private static readonly Regex LowerCaseGuid = new("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = DateTimeOffset.UtcNow };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> WithoutTheTimer() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Remove(services.Single(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(ListedJobAdSnapshotService)));
            services.RemoveAll<IDateTimeProvider>();
            services.AddSingleton<IDateTimeProvider>(_clock);
        }));

    [Fact]
    public async Task Before_any_read_a_part_is_a_503_with_Retry_After_and_no_store()
    {
        await using var host = WithoutTheTimer();
        using var client = host.CreateClient();

        var response = await client.GetAsync($"{Route}?part=1&parts={ListedJobAdSnapshotLimits.PartCount}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(60));
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task After_a_read_each_part_is_ids_alone_lower_case_in_id_order_and_only_the_listed_ones()
    {
        await using var host = WithoutTheTimer();
        using var client = host.CreateClient();
        var listed = await ImportAsync(host);
        var archived = await ImportAsync(host, ad => ad.Archive(_clock));
        var erased = await ImportAsync(host, ad => ad.Erase(_clock));
        var neverImported = await CreateManualAsync(host);
        await host.Services.GetRequiredService<IListedJobAdSnapshotSampler>().SampleAsync(Ct);

        var seen = new List<string>();
        for (var part = 1; part <= ListedJobAdSnapshotLimits.PartCount; part++)
        {
            var response = await client.GetAsync($"{Route}?part={part}&parts={ListedJobAdSnapshotLimits.PartCount}", Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["ids"],
                "ids and nothing else: no stamp, no count, no field of an ad");
            var ids = body.RootElement.GetProperty("ids").EnumerateArray().Select(e => e.GetString()!).ToList();
            ids.ShouldAllBe(id => LowerCaseGuid.IsMatch(id));
            ids.ShouldBe(ids.Order(StringComparer.Ordinal));
            ids.ShouldAllBe(id => ListedJobAdPartition.PartOf(Guid.Parse(id), ListedJobAdSnapshotLimits.PartCount) == part);
            seen.AddRange(ids);
        }

        seen.ShouldBeUnique();
        seen.ShouldContain(listed.ToString("D"));
        seen.ShouldNotContain(archived.ToString("D"));
        seen.ShouldNotContain(erased.ToString("D"));
        seen.ShouldNotContain(neverImported.ToString("D"));
    }

    [Fact]
    public async Task A_snapshot_older_than_its_bound_is_a_503()
    {
        await using var host = WithoutTheTimer();
        using var client = host.CreateClient();
        await host.Services.GetRequiredService<IListedJobAdSnapshotSampler>().SampleAsync(Ct);
        _clock.UtcNow += ListedJobAdSnapshotLimits.MaxAge + TimeSpan.FromSeconds(1);

        var response = await client.GetAsync($"{Route}?part=1&parts={ListedJobAdSnapshotLimits.PartCount}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("?part=0&parts=2")]
    [InlineData("?part=3&parts=2")]
    [InlineData("?part=1&parts=1")]
    [InlineData("?part=1&parts=4")]
    [InlineData("?part=abc&parts=2")]
    [InlineData("?part=1")]
    [InlineData("")]
    public async Task A_part_or_a_part_count_outside_the_sitemap_is_a_400_and_no_store(string query)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{Route}{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

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
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
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
}
