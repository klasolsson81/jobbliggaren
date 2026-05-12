using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Authorization;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

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
    public async Task Admin_triggers_snapshot_sync_updates_existing_via_unique_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var externalId = $"upd-{Guid.NewGuid():N}";

        // Pre-seed JobAd med samma ExternalId genom direct DB-write
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seedClock = seedScope.ServiceProvider
                .GetRequiredService<JobbPilot.Domain.Common.IDateTimeProvider>();
            var existing = JobAd.Import(
                title: "Old title",
                company: Company.Create("Old Co").Value,
                description: "Old desc",
                url: "https://old.example/1",
                external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
                rawPayload: "{\"old\":true}",
                publishedAt: seedClock.UtcNow.AddDays(-30),
                expiresAt: seedClock.UtcNow.AddDays(10),
                clock: seedClock).Value;
            seedDb.JobAds.Add(existing);
            await seedDb.SaveChangesAsync(ct);
        }

        var stubSnapshot = new[]
        {
            new JobAdImportItem(
                ExternalId: externalId,
                Title: "Updated title",
                CompanyName: "Updated Co",
                Description: "Updated desc",
                Url: "https://updated.example/1",
                PublishedAt: DateTimeOffset.UtcNow.AddDays(-30),
                ExpiresAt: DateTimeOffset.UtcNow.AddDays(60),
                SanitizedRawPayload: "{\"id\":\"" + externalId + "\",\"new\":true}"),
        };

        using var stubbedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IJobSource>();
                services.AddScoped<IJobSource>(_ => new StubJobSource(
                    JobSource.Platsbanken, stubSnapshot));
            });
        });

        var adminClient = await CreateAdminClientAsync(stubbedFactory.CreateClient(), ct);
        var response = await adminClient.PostAsync(
            "/api/v1/admin/job-ads/sync/platsbanken", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("addedCount").GetInt32().ShouldBe(0);
        json.GetProperty("updatedCount").GetInt32().ShouldBe(1);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.JobAds
            .FirstAsync(j => j.External!.ExternalId == externalId, ct);
        saved.Title.ShouldBe("Updated title");
        saved.Description.ShouldBe("Updated desc");
    }

    [Fact]
    public async Task Admin_triggers_snapshot_sync_and_persists_jobads()
    {
        var ct = TestContext.Current.CancellationToken;
        var externalId = $"test-{Guid.NewGuid():N}";
        var stubSnapshot = new[]
        {
            new JobAdImportItem(
                ExternalId: externalId,
                Title: "Senior Backend Developer",
                CompanyName: "Test Company AB",
                Description: "Test description.",
                Url: "https://example.com/job/123",
                PublishedAt: DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
                SanitizedRawPayload: "{\"id\":\"" + externalId + "\"}"),
        };

        using var stubbedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IJobSource>();
                services.AddScoped<IJobSource>(_ => new StubJobSource(
                    JobSource.Platsbanken, stubSnapshot));
            });
        });

        var adminClient = await CreateAdminClientAsync(stubbedFactory.CreateClient(), ct);

        var response = await adminClient.PostAsync(
            "/api/v1/admin/job-ads/sync/platsbanken", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("fetchedCount").GetInt32().ShouldBe(1);
        json.GetProperty("addedCount").GetInt32().ShouldBe(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.JobAds
            .FirstAsync(j => j.External!.ExternalId == externalId, ct);
        saved.Title.ShouldBe("Senior Backend Developer");
        saved.External!.Source.ShouldBe(JobSource.Platsbanken);
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
        var newSession = await sessionStore.CreateAsync(userId, ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newSession.Id.Reveal());

        return client;
    }
}
