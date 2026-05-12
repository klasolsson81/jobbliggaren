using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

[Collection("Api")]
public class ListJobAdsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GET_job_ads_returns_200_with_paged_result_shape()
    {
        var ct = TestContext.Current.CancellationToken;

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

        var response = await _client.GetAsync("/api/v1/job-ads?page=0", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_job_ads_with_invalid_pageSize_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/job-ads?pageSize=500", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
