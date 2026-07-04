using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// Admin snapshot-trigger-endpointen är avvecklad (ADR 0032 §9-amendment
/// 2026-05-16, senior-cto-advisor X4). Den körde snapshot synkront i requesten
/// (ALB-timeout) och dubblerade Hangfire-dashboardens "Trigger now". Snapshot
/// körs nu enbart via recurring-jobbet i Worker. Endpointen returnerar 410 Gone
/// men kräver fortfarande Admin-auth (gruppen RequireAuthorization).
/// </summary>
[Collection("Api")]
public class AdminSyncPlatsbankenTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/admin/job-ads/sync/platsbanken", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Non_admin_user_returns_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await client.PostAsync(
            "/api/v1/admin/job-ads/sync/platsbanken", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_request_returns_410_gone_pointing_to_dashboard()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        var response = await adminClient.PostAsync(
            "/api/v1/admin/job-ads/sync/platsbanken", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Gone);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("status").GetInt32().ShouldBe(410);
        var detail = problem.GetProperty("detail").GetString();
        detail.ShouldNotBeNull();
        detail.ShouldContain("sync-platsbanken-snapshot");
    }

    private async Task<HttpClient> CreateAdminClientAsync(HttpClient client, CancellationToken ct)
    {
        var email = $"admin-jobads-{Guid.NewGuid():N}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var me = await client.GetAsync("/api/v1/me", ct);
        me.EnsureSuccessStatusCode();
        var meJson = await me.Content.ReadFromJsonAsync<JsonElement>(ct);
        var userId = Guid.Parse(meJson.GetProperty("userId").GetString()!);

        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(Roles.Admin))
            await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin));
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User saknas.");
        await userManager.AddToRoleAsync(user, Roles.Admin);

        var sessionStore = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var newSession = await sessionStore.CreateAsync(userId, SessionLifetime.Legacy, ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newSession.Id.Reveal());

        return client;
    }
}
