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
/// Integration-tester för <c>SessionRoleClaimsTransformation</c> (ADR 0029).
///
/// <para>
/// Verifierar HTTP-auth-pipeline-disciplinen end-to-end:
/// <list type="bullet">
///   <item>Anonyma requests passerar transformation utan effekt (early return på <c>IsAuthenticated == false</c>).</item>
///   <item>Autentiserade users utan roller får INTE Role-claims populerade (admin-endpoint → 403).</item>
///   <item>Autentiserade users med Admin-roll får Role-claim populerad (admin-endpoint → 200).</item>
///   <item>Per-request-fetch-disciplin: roll-revoke verkar omedelbart (ADR 0029 Beslut 3, ADR 0028 §1).</item>
///   <item>Pipeline-ordning: transformation körs efter authentication-success och före authorization-policy
///         (ADR 0029 Beslut 1) — verifieras indirekt via 401-vs-403-distinktion + admin-200-success.</item>
/// </list>
/// </para>
///
/// <para>
/// Komplementär till <see cref="Admin.AdminAuditLogTests"/> som täcker admin-endpoint-flödet
/// från slutanvändar-perspektiv. Denna fil dokumenterar explicit transformation-rollen i ADR 0029-termer.
/// </para>
///
/// <para>
/// Sentinel-claim-idempotens (<c>jobbliggaren:roles_resolved</c>) verifieras via kod-kommentar
/// i <c>SessionRoleClaimsTransformation.cs:33-38</c> + per-request-fetch-test nedan. ASP.NET-pipelinen
/// trigger:ar inte status-code re-execution i den normala request-vägen som integration-test exercise:ar,
/// så call-count-verifiering kräver dedikerad enhetstest mot transformation-klassen i isolation
/// (defererat — kräver Infrastructure.UnitTests-projekt som inte existerar idag).
/// </para>
/// </summary>
[Collection("Api")]
public class SessionRoleClaimsTransformationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private const string AdminEndpoint = "/api/v1/admin/audit-log?pageSize=1";
    private const string AnonymousReadyEndpoint = "/api/ready";
    private const string AuthenticatedSelfEndpoint = "/api/v1/me";

    private async Task<(HttpClient client, Guid userId)> RegisterAuthenticatedClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"trans-{Guid.NewGuid():N}@jobbliggaren.test";
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
    public async Task Anonymous_request_to_anonymous_endpoint_succeeds_without_transformation_effect()
    {
        // ADR 0029 Beslut 1 + SessionRoleClaimsTransformation.cs:42-43:
        // transformation gör early-return när IsAuthenticated == false. Verifieras
        // indirekt: anonyma request till /api/ready ska få 200 utan att transformation
        // crashar eller blockerar (om transformation skulle exception:a på anonymous
        // principal skulle anonymous endpoints sluta fungera).
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(AnonymousReadyEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authenticated_user_without_admin_role_does_not_receive_admin_role_claim()
    {
        // ADR 0029 Beslut 2 + SessionRoleClaimsTransformation.cs:62-65:
        // transformation anropar GetRolesAsync och adderar exakt de roller usern har.
        // Rolless user ska INTE få falska Role-claims → admin-policy fail → 403.
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterAuthenticatedClientAsync(ct);

        var response = await client.GetAsync(AdminEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authenticated_user_with_admin_role_receives_admin_role_claim()
    {
        // ADR 0029 Beslut 2 + Beslut 1 (pipeline-ordning):
        // transformation populerar ClaimTypes.Role efter authentication-success,
        // FÖRE authorization-policy-utvärdering. Om transformation kördes efter
        // policy:n skulle admin-users alltid få 403. 200-svaret bevisar att
        // claim-population sker i rätt pipeline-position.
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterAuthenticatedClientAsync(ct);
        await PromoteToAdminAsync(userId, ct);

        var response = await client.GetAsync(AdminEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Role_revoke_takes_effect_on_next_request_proving_per_request_fetch()
    {
        // ADR 0029 Beslut 3 + ADR 0028 §1 (security-first per Microsoft Learn):
        // transformation gör per-request DB-query mot AspNetUserRoles. Roll-revoke
        // verkar på NÄSTA request, inte efter session-refresh. Ingen cache.
        //
        // NOT: AdminAuditLogTests.GetAuditLog_AfterRoleRevoke_Returns403OnNextRequest
        // har samma assertion. Detta test re-asserterar disciplin från transformation-
        // perspektiv så framtida cache-introduktion (separat ADR per Beslut 3-trigger)
        // bryter BÅDA testfilerna och tvingar medveten review.
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
        // ADR 0029 Beslut 1 (pipeline-ordning):
        // UseAuthentication kör FÖRE UseAuthorization. Anonym request stoppas
        // av authentication-middleware (401), inte av authorization-policy (403).
        // Distinktionen är säkerhetskritisk för korrekt HTTP-protokoll-respons
        // (RFC 7235 — 401 challenge vs 403 forbidden) och bekräftar att
        // transformation aldrig körs på unauthenticated principal.
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(AdminEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
