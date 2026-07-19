using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// Integration tests for the Admin authorization path (#746 PR-B — lazy role resolution).
///
/// <para>
/// Since #746 PR-B, roles are resolved ON DEMAND by <c>AdminRoleAuthorizationHandler</c> when the Admin
/// policy is evaluated, replacing the eager <c>SessionRoleClaimsTransformation</c> that ran on every
/// authenticated request. These tests verify the HTTP auth-pipeline discipline end-to-end:
/// <list type="bullet">
///   <item>Anonymous requests reach anonymous endpoints unaffected (the handler never runs unauthenticated).</item>
///   <item>An authenticated user WITHOUT the Admin role does not satisfy the policy → admin endpoint 403.</item>
///   <item>An authenticated user WITH the Admin role has the role resolved + attached → admin endpoint 200,
///         proving the handler runs before the endpoint (both the HTTP policy and the downstream Mediator
///         AdminAuthorizationBehavior see the role).</item>
///   <item>Per-request resolution: a role revoke takes effect immediately, no cache (A1 2026-05-11).</item>
///   <item>Anonymous on an admin endpoint → 401 (challenge), not 403 — UseAuthentication before UseAuthorization;
///         RequireAuthenticatedUser makes the split explicit with no DB call.</item>
/// </list>
/// </para>
///
/// <para>
/// Complementary to <see cref="Admin.AdminAuditLogTests"/> (admin-endpoint flow from the end-user view) and
/// <c>AdminRoleLazyResolutionCountTests</c> (the d2/d4 counterfactual: a non-admin request resolves zero roles).
/// </para>
/// </summary>
[Collection("Api")]
public class AdminRoleAuthorizationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private const string AdminEndpoint = "/api/v1/admin/audit-log?pageSize=1";
    private const string AnonymousReadyEndpoint = "/api/ready";
    private const string AuthenticatedSelfEndpoint = "/api/v1/me";

    private async Task<(HttpClient client, Guid userId)> RegisterAuthenticatedClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"authz-{Guid.NewGuid():N}@jobbliggaren.test";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var me = await client.GetAsync(AuthenticatedSelfEndpoint, ct);
        me.EnsureSuccessStatusCode();
        var meJson = await me.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        var userId = Guid.Parse(meJson.GetProperty("userId").GetString()!);

        return (client, userId);
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
    public async Task Anonymous_request_to_anonymous_endpoint_succeeds()
    {
        // The Admin handler never runs on an anonymous/open endpoint. /api/ready must stay 200.
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(AnonymousReadyEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authenticated_user_without_admin_role_is_forbidden_on_admin_endpoint()
    {
        // AdminRoleAuthorizationHandler resolves the user's roles on demand; a role-less user does not
        // get the Admin role → the requirement stays unmet → 403.
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterAuthenticatedClientAsync(ct);

        var response = await client.GetAsync(AdminEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authenticated_admin_user_is_authorized_on_admin_endpoint()
    {
        // The handler resolves + attaches ClaimTypes.Role BEFORE the endpoint runs, so both the HTTP
        // Admin policy AND the downstream Mediator AdminAuthorizationBehavior (via ICurrentUser.IsInRole)
        // pass → 200. Were resolution to run after the policy, an admin would always get 403.
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterAuthenticatedClientAsync(ct);
        await PromoteToAdminAsync(userId, ct);

        var response = await client.GetAsync(AdminEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Role_revoke_takes_effect_on_next_request_proving_per_request_resolution()
    {
        // Per-request on-demand resolution (A1, security-first): a role revoke takes effect on the NEXT
        // request, no cache. A future cache introduction (separate ADR) would break this + AdminAuditLogTests,
        // forcing conscious review.
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterAuthenticatedClientAsync(ct);
        await PromoteToAdminAsync(userId, ct);

        var beforeRevoke = await client.GetAsync(AdminEndpoint, ct);
        beforeRevoke.StatusCode.ShouldBe(HttpStatusCode.OK);

        await DemoteFromAdminAsync(userId, ct);

        var afterRevoke = await client.GetAsync(AdminEndpoint, ct);
        afterRevoke.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_request_to_admin_endpoint_returns_401_not_403()
    {
        // UseAuthentication runs BEFORE UseAuthorization: an anonymous request is challenged (401) rather
        // than forbidden (403). RequireAuthenticatedUser makes the split explicit (no DB call), and the
        // distinction (RFC 7235) confirms the handler never runs on an unauthenticated principal.
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(AdminEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
