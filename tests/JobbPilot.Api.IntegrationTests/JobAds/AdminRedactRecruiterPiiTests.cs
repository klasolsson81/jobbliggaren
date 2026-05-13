using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Authorization;
using JobbPilot.Application.JobAds.Commands.RedactRecruiterPii;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

/// <summary>
/// TD-73 prod-gating — admin endpoint POST /api/v1/admin/job-ads/redact-recruiter-pii.
/// Per ADR 0032 §8 amendment 2026-05-13 + ADR 0035 + CTO Q2 (total null-out) + Q3 (aggregerad audit).
///
/// Verifierar end-to-end:
/// <list type="bullet">
/// <item>Email-typ matchar JobAds via EF.Functions.JsonContains mot Postgres jsonb</item>
/// <item>Total null-out av raw_payload (CTO Q2)</item>
/// <item>Auth: 401 anonymous, 403 non-admin, 200 admin</item>
/// <item>Aggregerad audit-rad skrivs via AuditBehavior (Admin.RecruiterPiiRedacted)</item>
/// <item>Name-typ → 400 NameNotSupportedYet (TD-75-deferral)</item>
/// </list>
/// </summary>
[Collection("Api")]
public class AdminRedactRecruiterPiiTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii",
            new { identifier = "alice@example.com", type = "Email" }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Non_admin_returns_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii",
            new { identifier = "alice@example.com", type = "Email" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_redact_by_email_nulls_matching_raw_payload_and_leaves_non_matching()
    {
        var ct = TestContext.Current.CancellationToken;
        var targetEmail = $"alice-{Guid.NewGuid():N}@example.com";
        var otherEmail = $"bob-{Guid.NewGuid():N}@example.com";

        var (matchingId, nonMatchingId) = await SeedJobAdsWithRecruiterEmailsAsync(targetEmail, otherEmail, ct);

        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);
        var response = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii",
            new { identifier = targetEmail, type = "Email" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("rowsAffected").GetInt32().ShouldBe(1);
        json.GetProperty("requestId").GetGuid().ShouldNotBe(Guid.Empty);

        // Verifiera DB-state
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var matching = await db.JobAds.IgnoreQueryFilters()
            .FirstAsync(j => j.External!.ExternalId == matchingId, ct);
        var nonMatching = await db.JobAds.IgnoreQueryFilters()
            .FirstAsync(j => j.External!.ExternalId == nonMatchingId, ct);

        matching.RawPayload.ShouldBeNull("Matching row should be null:ad");
        nonMatching.RawPayload.ShouldNotBeNull("Non-matching row preserved");
    }

    [Fact]
    public async Task Admin_redact_by_email_writes_audit_row_with_correct_event_type()
    {
        var ct = TestContext.Current.CancellationToken;
        var targetEmail = $"alice-{Guid.NewGuid():N}@example.com";

        await SeedJobAdsWithRecruiterEmailsAsync(targetEmail, otherEmail: null, ct);

        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);
        var response = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii",
            new { identifier = targetEmail, type = "Email" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var requestId = json.GetProperty("requestId").GetGuid();

        // Verifiera audit-rad — aggregerad EN rad per request (CTO Q3=B)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditRow = await db.AuditLogEntries
            .FirstOrDefaultAsync(a => a.AggregateId == requestId, ct);

        auditRow.ShouldNotBeNull();
        auditRow.EventType.ShouldBe("Admin.RecruiterPiiRedacted");
        auditRow.AggregateType.ShouldBe("System.RecruiterPiiRedaction");
    }

    [Fact]
    public async Task Admin_redact_by_name_returns_400_NameNotSupportedYet()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        var response = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii",
            new { identifier = "Alice Anka", type = "Name" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("title").GetString().ShouldBe("RedactRecruiterPii.NameNotSupportedYet");
    }

    [Fact]
    public async Task Admin_redact_with_no_matches_returns_zero_rowsAffected()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminClient = await CreateAdminClientAsync(_factory.CreateClient(), ct);

        var response = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/job-ads/redact-recruiter-pii",
            new { identifier = $"nonexistent-{Guid.NewGuid():N}@example.com", type = "Email" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("rowsAffected").GetInt32().ShouldBe(0);
    }

    private async Task<(string MatchingExternalId, string NonMatchingExternalId)>
        SeedJobAdsWithRecruiterEmailsAsync(string targetEmail, string? otherEmail, CancellationToken ct)
    {
        var matchingId = $"match-{Guid.NewGuid():N}";
        var nonMatchingId = $"nomatch-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider
            .GetRequiredService<JobbPilot.Domain.Common.IDateTimeProvider>();

        var matchingPayload = JsonSerializer.Serialize(new
        {
            id = matchingId,
            employer = new { contact_email = targetEmail.ToLowerInvariant() },
        });
        var matching = JobAd.Import(
            title: "Match", company: Company.Create("X").Value,
            description: "d", url: "https://example.com/m",
            external: ExternalReference.Create(JobSource.Platsbanken, matchingId).Value,
            rawPayload: matchingPayload,
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;
        db.JobAds.Add(matching);

        if (otherEmail is not null)
        {
            var otherPayload = JsonSerializer.Serialize(new
            {
                id = nonMatchingId,
                employer = new { contact_email = otherEmail.ToLowerInvariant() },
            });
            var nonMatching = JobAd.Import(
                title: "NonMatch", company: Company.Create("Y").Value,
                description: "d", url: "https://example.com/n",
                external: ExternalReference.Create(JobSource.Platsbanken, nonMatchingId).Value,
                rawPayload: otherPayload,
                publishedAt: clock.UtcNow.AddDays(-1),
                expiresAt: clock.UtcNow.AddDays(30),
                clock: clock).Value;
            db.JobAds.Add(nonMatching);
        }

        await db.SaveChangesAsync(ct);
        return (matchingId, nonMatchingId);
    }

    private async Task<HttpClient> CreateAdminClientAsync(HttpClient client, CancellationToken ct)
    {
        var email = $"admin-redact-{Guid.NewGuid():N}@example.com";
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
