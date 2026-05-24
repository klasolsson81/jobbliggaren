using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Authorization;
using JobbPilot.Domain.Waitlist;
using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Waitlist;

[Collection("Api")]
public class AdminWaitlistApprovalTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Admin_approves_pending_waitlist_entry_creates_invitation()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"admin-approve-{Guid.NewGuid()}@example.com";
        var entryId = await SeedPendingEntryAsync(email, ct);

        var adminClient = await CreateAdminClientAsync(ct);
        var response = await adminClient.PostAsJsonAsync(
            $"/api/v1/admin/waitlist/{entryId}/approve",
            new { validForDays = 7 },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("invitationId").GetGuid().ShouldNotBe(Guid.Empty);
        json.GetProperty("email").GetString().ShouldBe(email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entryIdValue = new WaitlistEntryId(entryId);
        var saved = await db.WaitlistEntries.FirstAsync(w => w.Id == entryIdValue, ct);
        saved.Status.ShouldBe(WaitlistStatus.Approved);
        saved.ResultingInvitationId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Admin_rejects_pending_waitlist_entry_returns_204()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"admin-reject-{Guid.NewGuid()}@example.com";
        var entryId = await SeedPendingEntryAsync(email, ct);

        var adminClient = await CreateAdminClientAsync(ct);
        var response = await adminClient.PostAsync(
            $"/api/v1/admin/waitlist/{entryId}/reject", null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entryIdValue = new WaitlistEntryId(entryId);
        var saved = await db.WaitlistEntries.FirstAsync(w => w.Id == entryIdValue, ct);
        saved.Status.ShouldBe(WaitlistStatus.Rejected);
    }

    [Fact]
    public async Task Admin_lists_waitlist_filtered_by_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPendingEntryAsync($"list-{Guid.NewGuid()}@example.com", ct);

        var adminClient = await CreateAdminClientAsync(ct);
        var response = await adminClient.GetAsync("/api/v1/admin/waitlist/?status=Pending", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        items.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Anonymous_request_to_admin_endpoint_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/admin/waitlist/", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- helpers ---

    private async Task<Guid> SeedPendingEntryAsync(string email, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<JobbPilot.Domain.Common.IDateTimeProvider>();
        var acceptance = new AcceptanceSnapshot(
            MarketingEmailAccepted: false,
            AcceptedAt: clock.UtcNow,
            PrivacyPolicyVersion: "1.0");
        var entry = WaitlistEntry.Request(
            email,
            name: "Testperson",
            motivation: "Integration-test seed med tillräckligt lång motivering.",
            acceptance,
            clock).Value;
        db.WaitlistEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry.Id.Value;
    }

    private async Task<HttpClient> CreateAdminClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"admin-wl-{Guid.NewGuid():N}@example.com";
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
        var newSession = await sessionStore.CreateAsync(userId, ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newSession.Id.Reveal());

        return client;
    }
}
