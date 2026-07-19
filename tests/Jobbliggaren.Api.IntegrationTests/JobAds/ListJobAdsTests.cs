using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

[Collection("Api")]
public class ListJobAdsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task GET_job_ads_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/job-ads", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_job_ads_with_auth_returns_200_with_paged_result_shape()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Object);
        json.TryGetProperty("items", out var items).ShouldBeTrue();
        items.ValueKind.ShouldBe(JsonValueKind.Array);
        json.TryGetProperty("totalCount", out _).ShouldBeTrue();
        json.TryGetProperty("page", out _).ShouldBeTrue();
        json.TryGetProperty("pageSize", out _).ShouldBeTrue();
        json.TryGetProperty("totalPages", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task GET_job_ads_honors_pagination_query_params()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads?page=2&pageSize=5", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("page").GetInt32().ShouldBe(2);
        json.GetProperty("pageSize").GetInt32().ShouldBe(5);
    }

    [Fact]
    public async Task GET_job_ads_with_invalid_page_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads?page=0", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_job_ads_with_invalid_pageSize_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads?pageSize=500", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // #745 (epic #737, finding d1-list-dto-ships-full-description) — the split, asserted at the
    // HTTP/JSON boundary: the serialized LIST row carries title + companyName but NO `description`
    // (no list surface renders the ad body; shipping the untruncated Description per row de-TOASTed a
    // wide column for a payload nothing reads), while the DETAIL endpoint for the SAME ad still
    // returns the full body (counterfactual — the split keeps Description on the detail wire only).
    // Isolated by a unique title marker (>=3 chars → matched via the title-LIKE fallback regardless
    // of FTS tokenization) so shared-DB contamination cannot add a second match.
    [Fact]
    public async Task GET_job_ads_list_omits_description_but_detail_returns_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var marker = $"listdto{Guid.NewGuid():N}"[..18];
        const string body = "Hemlig annonstext som bara detaljvyn ska returnera.";
        var id = await SeedJobAdAsync($"Utvecklare {marker}", body, ct);

        // LIST — the row for our ad has title + companyName but no description property at all.
        var listResponse = await _client.GetAsync($"/api/v1/job-ads?q={marker}", ct);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        list.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var item = list.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("title").GetString().ShouldBe($"Utvecklare {marker}");
        item.TryGetProperty("companyName", out _).ShouldBeTrue();
        item.TryGetProperty("description", out _).ShouldBeFalse(
            "the LIST wire must not serialize the ad Description (#745/#737 d1)");

        // DETAIL (same ad) — the full Description is still returned.
        var detailResponse = await _client.GetAsync($"/api/v1/job-ads/{id}", ct);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        detail.GetProperty("description").GetString().ShouldBe(body);
    }

    private async Task<Guid> SeedJobAdAsync(string title, string description, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var jobAd = JobAd.Create(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: description,
            url: $"https://example.com/jobs/{Guid.NewGuid():N}",
            source: JobSource.Manual,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: null,
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }
}
