using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// Integration-tester för /api/v1/admin/audit-log per Fas 1-milestonstängning.
/// Verifierar:
/// - 401 utan auth-header
/// - 403 för autentiserad icke-Admin
/// - 200 för Admin + paginerings-struktur + filtrering
/// - Roll-revoke-immediacy (CTO A1-invariant: roll-borttagning verkar omedelbart
///   utan session-refresh)
/// </summary>
[Collection("Api")]
public class AdminAuditLogTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<(HttpClient client, Guid userId, string email)> RegisterUserAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"admin-audit-{Guid.NewGuid():N}@jobbliggaren.test";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var me = await client.GetAsync("/api/v1/me", ct);
        me.EnsureSuccessStatusCode();
        var meJson = await me.Content.ReadFromJsonAsync<JsonElement>(ct);
        var userId = Guid.Parse(meJson.GetProperty("userId").GetString()!);

        return (client, userId, email);
    }

    private async Task PromoteToAdminAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(Roles.Admin))
            (await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin))).Succeeded.ShouldBeTrue();

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");
        (await userManager.AddToRoleAsync(user, Roles.Admin)).Succeeded.ShouldBeTrue();
    }

    private async Task DemoteFromAdminAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");
        (await userManager.RemoveFromRoleAsync(user, Roles.Admin)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAuditLog_WithoutAuth_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/audit-log", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAuditLog_WithAuthenticatedNonAdmin_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _) = await RegisterUserAsync(ct);

        var response = await client.GetAsync("/api/v1/admin/audit-log", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAuditLog_WithAdminRole_Returns200WithPagedShape()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId, _) = await RegisterUserAsync(ct);
        await PromoteToAdminAsync(userId, ct);

        // Trigga audit-skrivning innan vi läser så att vyn har data
        var post = await client.PostAsJsonAsync(
            "/api/v1/applications",
            new { jobAdId = (Guid?)null, coverLetter = (string?)null },
            ct);
        post.StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await client.GetAsync("/api/v1/admin/audit-log?page=1&pageSize=10", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("items").GetArrayLength().ShouldBeGreaterThan(0);
        json.GetProperty("totalCount").GetInt32().ShouldBeGreaterThan(0);
        json.GetProperty("page").GetInt32().ShouldBe(1);
        json.GetProperty("pageSize").GetInt32().ShouldBe(10);
    }

    [Fact]
    public async Task GetAuditLog_FilteredByEventType_OnlyReturnsMatchingEntries()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId, _) = await RegisterUserAsync(ct);
        await PromoteToAdminAsync(userId, ct);

        // Två olika event-types: Application.Created + Resume.Created
        await client.PostAsJsonAsync(
            "/api/v1/applications",
            new { jobAdId = (Guid?)null, coverLetter = (string?)null },
            ct);
        await client.PostAsJsonAsync(
            "/api/v1/resumes",
            new { name = "Filter-test-CV", fullName = "Test" },
            ct);

        var response = await client.GetAsync(
            "/api/v1/admin/audit-log?eventType=Application.Created&pageSize=50", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var items = json.GetProperty("items").EnumerateArray().ToList();
        items.ShouldAllBe(i => i.GetProperty("eventType").GetString() == "Application.Created");
    }

    [Fact]
    public async Task GetAuditLog_AfterRoleRevoke_Returns403OnNextRequest()
    {
        // CTO A1-invariant (senior-cto-advisor-beslut 2026-05-11): roll-revoke
        // verkar omedelbart utan session-refresh — admin-yta får 403 på nästa
        // request efter rolltilldelning tagits bort.
        var ct = TestContext.Current.CancellationToken;
        var (client, userId, _) = await RegisterUserAsync(ct);

        await PromoteToAdminAsync(userId, ct);
        var beforeRevoke = await client.GetAsync("/api/v1/admin/audit-log?pageSize=1", ct);
        beforeRevoke.StatusCode.ShouldBe(HttpStatusCode.OK);

        await DemoteFromAdminAsync(userId, ct);

        var afterRevoke = await client.GetAsync("/api/v1/admin/audit-log?pageSize=1", ct);
        afterRevoke.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAuditLog_InvalidPaginationParameters_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId, _) = await RegisterUserAsync(ct);
        await PromoteToAdminAsync(userId, ct);

        var pageZero = await client.GetAsync("/api/v1/admin/audit-log?page=0", ct);
        pageZero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var pageSizeTooLarge = await client.GetAsync("/api/v1/admin/audit-log?pageSize=500", ct);
        pageSizeTooLarge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetAuditLog_FromAfterTo_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId, _) = await RegisterUserAsync(ct);
        await PromoteToAdminAsync(userId, ct);

        var from = DateTimeOffset.UtcNow;
        var to = from.AddDays(-1);
        var url = $"/api/v1/admin/audit-log?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

        var response = await client.GetAsync(url, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
