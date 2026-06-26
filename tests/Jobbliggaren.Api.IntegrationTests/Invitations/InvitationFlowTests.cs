using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.Invitations;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Invitations;

/// <summary>
/// End-to-end-flöde: admin utfärdar invitation → mottagare löser in token →
/// session skapas. Plus revoke-flöde.
/// </summary>
[Collection("Api")]
public class InvitationFlowTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Admin_issues_invitation_and_recipient_redeems_successfully()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(ct);

        var inviteeEmail = $"invitee-{Guid.NewGuid()}@example.com";
        var issueResp = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/invitations/",
            new { email = inviteeEmail, validForDays = 7 },
            ct);
        issueResp.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Hämta plaintext-token via TokenGenerator-port (i prod skickas den via email;
        // i test plockar vi hash från DB och rekonstruerar inte plaintext — istället
        // genererar vi ett nytt token-par och seedar Invitation direkt). För
        // happy-path-testet använder vi en parallell väg: seeda invitation manuellt
        // i DB med känt plaintext + hash från IInvitationTokenGenerator.
        var (plaintext, redeemResp) = await SeedAndRedeemAsync(inviteeEmail, ct);

        redeemResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await redeemResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("sessionId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Redeem_unknown_token_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        // Generera giltig-formatterad men icke-existerande token
        using var scope = _factory.Services.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<IInvitationTokenGenerator>();
        var token = gen.Generate();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/redeem-invitation",
            new { token = token.Plaintext, password = AuthTestHelpers.DefaultTestPassword, displayName = "Test" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_revoke_pending_invitation_returns_204_and_subsequent_redeem_returns_410()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(ct);

        var inviteeEmail = $"revoke-{Guid.NewGuid()}@example.com";
        var (plaintext, invitationId) = await SeedInvitationAsync(inviteeEmail, ct);

        var revokeResp = await adminClient.PostAsync(
            $"/api/v1/admin/invitations/{invitationId}/revoke", null, ct);
        revokeResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var anonClient = _factory.CreateClient();
        var redeemResp = await anonClient.PostAsJsonAsync(
            "/api/v1/auth/redeem-invitation",
            new { token = plaintext, password = AuthTestHelpers.DefaultTestPassword, displayName = "Test" },
            ct);

        redeemResp.StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Admin_revoke_already_revoked_invitation_returns_409()
    {
        // #239 fold-lock — AdminInvitations' bespoke Code switch-mapper folded into the central
        // DomainError.ToProblemResult(). Invitation.NotPending (Kind.Conflict) → 409 through the
        // central kind-mapper (RFC 9110 §15.5.10), behaviour unchanged but now proven through the
        // central path. Email-free: revoke sends no mail and the invitation is seeded directly (no
        // issue-email), so it probes the Conflict→409 fold without the ResendEmailSender path.
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(ct);
        var (_, invitationId) = await SeedInvitationAsync($"rerevoke-{Guid.NewGuid()}@example.com", ct);

        var first = await adminClient.PostAsync(
            $"/api/v1/admin/invitations/{invitationId}/revoke", null, ct);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await adminClient.PostAsync(
            $"/api/v1/admin/invitations/{invitationId}/revoke", null, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // --- helpers ---

    private async Task<HttpClient> CreateAdminClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"admin-inv-{Guid.NewGuid():N}@example.com";
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

        // Re-issue session så roll-claim:en plockas upp av next request.
        var sessionStore = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var newSession = await sessionStore.CreateAsync(userId, ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newSession.Id.Reveal());

        return client;
    }

    private async Task<(string Plaintext, Guid InvitationId)> SeedInvitationAsync(
        string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IInvitationTokenGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<Jobbliggaren.Domain.Common.IDateTimeProvider>();

        var token = gen.Generate();
        var invitation = Invitation.Issue(
            email, InvitationOrigin.DirectInvite, token.Hash,
            TimeSpan.FromDays(7), Guid.NewGuid(), clock).Value;
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        return (token.Plaintext, invitation.Id.Value);
    }

    private async Task<(string Plaintext, HttpResponseMessage Response)> SeedAndRedeemAsync(
        string email, CancellationToken ct)
    {
        var (plaintext, _) = await SeedInvitationAsync(email, ct);
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/redeem-invitation",
            new { token = plaintext, password = AuthTestHelpers.DefaultTestPassword, displayName = "Redeemed User" },
            ct);
        return (plaintext, response);
    }
}
