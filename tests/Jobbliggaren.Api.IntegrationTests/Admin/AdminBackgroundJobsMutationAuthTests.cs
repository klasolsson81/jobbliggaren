using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.BackgroundJobs;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #204 / TD-83 PR2 — auth-gate tests for the MUTATION operator surface
/// (POST .../recurring/{id}/trigger + POST .../failed/{jobId}/retry). Proves the two outer layers
/// of security-auditor's must-clear auth gate: 401 without an auth header, 403 for an authenticated
/// non-Admin. Both are rejected by <c>RequireAuthorization(Admin)</c> on the group BEFORE the handler
/// (and the IBackgroundJobController fake) runs, so these need no hangfire schema. Mirrors PR1's
/// <see cref="AdminBackgroundJobsAuthTests"/> (GET surface) and <see cref="AdminAuditLogTests"/>.
/// </summary>
[Collection("Api")]
public class AdminBackgroundJobsMutationAuthTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // A valid allowlisted id so a non-allowlist 400 can never mask an auth status. The body is empty;
    // the ids ride the route. The retry job id is an arbitrary short token (auth runs before shape).
    public static TheoryData<string> MutationEndpoints =>
    [
        $"/api/v1/admin/jobs/recurring/{RecurringJobIds.BackgroundMatching}/trigger",
        "/api/v1/admin/jobs/failed/server%3A1%3Ajob%3A42/retry",
    ];

    [Theory]
    [MemberData(nameof(MutationEndpoints))]
    public async Task Post_WithoutAuth_Returns401(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsync(url, content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(MutationEndpoints))]
    public async Task Post_WithAuthenticatedNonAdmin_Returns403(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterNonAdminClientAsync(ct);

        var response = await client.PostAsync(url, content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> RegisterNonAdminClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"admin-jobs-mut-{Guid.NewGuid():N}@jobbliggaren.test";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        // Confirm the client is actually authenticated (non-Admin) so the 403 is a role denial, not a
        // missing auth header (which would yield 401).
        var me = await client.GetAsync("/api/v1/me", ct);
        me.EnsureSuccessStatusCode();
        var meJson = await me.Content.ReadFromJsonAsync<JsonElement>(ct);
        meJson.GetProperty("userId").GetString().ShouldNotBeNullOrEmpty();

        return client;
    }
}
