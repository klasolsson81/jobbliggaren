using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.UserStatus;

/// <summary>
/// ADR 0063 — per-user-overlay-status batch-port + has-applied single.
/// End-to-end mot Testcontainers Postgres. Verifierar både happy paths
/// och de tre auth-konfigurationerna: batch är ANONYM-TILLGÄNGLIG
/// (ADR 0063 §Kontext "no 401-friktion"), modal-single är auth-gated.
/// </summary>
[Collection("Api")]
public class JobAdStatusEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task POST_job_ad_status_without_auth_returns_200_with_empty_dto()
    {
        // ADR 0063 §Kontext: anonym → tom DTO (no 401-friktion på publik söksida).
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/job-ad-status",
            new { jobAdIds = new[] { Guid.NewGuid() } },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        dto.GetProperty("savedIds").GetArrayLength().ShouldBe(0);
        dto.GetProperty("appliedIds").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task POST_job_ad_status_with_empty_batch_returns_empty_dto()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/job-ad-status",
            new { jobAdIds = Array.Empty<Guid>() },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        dto.GetProperty("savedIds").GetArrayLength().ShouldBe(0);
        dto.GetProperty("appliedIds").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task POST_job_ad_status_over_100_ids_returns_400()
    {
        // Validator-cap (GetJobAdStatusBatchQueryValidator.MaxJobAdIdsPerCall = 100).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/job-ad-status",
            new { jobAdIds = ids },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_job_ad_status_returns_savedIds_for_authenticated_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Hämta en jobAd från publika listan + spara den
        var listResp = await _client.GetAsync("/api/v1/job-ads?page=1&pageSize=5", ct);
        listResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listDto = await listResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var items = listDto.GetProperty("items");
        if (items.GetArrayLength() == 0) return; // Tom korpus i CI — skip

        var jobAdId = items[0].GetProperty("id").GetString()!;
        var saveResp = await _client.PostAsync(
            $"/api/v1/me/saved-job-ads/{jobAdId}", content: null, ct);
        saveResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Batch-query ska nu inkludera jobAdId i savedIds
        var statusResp = await _client.PostAsJsonAsync(
            "/api/v1/me/job-ad-status",
            new { jobAdIds = new[] { jobAdId } },
            ct);
        statusResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var statusDto = await statusResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        statusDto.GetProperty("savedIds").GetArrayLength().ShouldBe(1);
        statusDto.GetProperty("savedIds")[0].GetString().ShouldBe(jobAdId);
        statusDto.GetProperty("appliedIds").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task GET_has_applied_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync(
            $"/api/v1/me/applications/has-applied/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_has_applied_for_unknown_jobad_returns_false()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            $"/api/v1/me/applications/has-applied/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        dto.GetProperty("hasApplied").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task GET_has_applied_returns_true_after_creating_application_from_jobad()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var listResp = await _client.GetAsync("/api/v1/job-ads?page=1&pageSize=5", ct);
        var listDto = await listResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var items = listDto.GetProperty("items");
        if (items.GetArrayLength() == 0) return;

        var jobAdId = items[0].GetProperty("id").GetString()!;
        var createResp = await _client.PostAsync(
            $"/api/v1/applications/from-job-ad/{jobAdId}", content: null, ct);
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);

        var hasAppliedResp = await _client.GetAsync(
            $"/api/v1/me/applications/has-applied/{jobAdId}", ct);
        hasAppliedResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await hasAppliedResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        dto.GetProperty("hasApplied").GetBoolean().ShouldBeTrue();
    }
}
