using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// #782 (ADR 0104) — HTTP + relational wiring for the per-application HARD delete
// (DELETE /api/v1/applications/{id}). Covers: auth, the 204 mapping, list/detail
// exclusion, the Application.Deleted audit row, the cross-user 404 (no enumeration
// oracle), and — the load-bearing HARD assertion — that the root AND its three
// child tables (follow_ups / application_notes / application_status_changes) are
// PHYSICALLY removed via the DB FK cascade (IgnoreQueryFilters count == 0), i.e.
// a hard delete, not a soft one. The relational cascade cannot be proven by the
// InMemory unit test → it MUST run against Testcontainers Postgres.
[Collection("Api")]
public class DeleteApplicationEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<HttpClient> AuthenticatedClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"delete-app-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private static async Task<string> CreateManualApplicationAsync(HttpClient client, CancellationToken ct)
    {
        var post = await client.PostAsJsonAsync(
            "/api/v1/applications",
            new
            {
                jobAdId = (Guid?)null,
                coverLetter = (string?)null,
                manual = new { title = "Backend-utvecklare", company = "Acme AB", url = (string?)null, expiresAt = (DateTimeOffset?)null },
            },
            ct);
        post.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await post.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
    }

    // Counts the child rows (follow_ups / application_notes / application_status_changes)
    // still bound to an application, bypassing the soft-delete query filters. The shadow FK
    // "ApplicationId" is the strongly-typed value object (it mirrors the principal key type),
    // so the predicate compares against the value object, not a raw Guid.
    private static async Task<(int followUps, int notes, int statusChanges)> ChildCountsAsync(
        AppDbContext db, Guid appGuid, CancellationToken ct)
    {
        var appIdVo = new Jobbliggaren.Domain.Applications.ApplicationId(appGuid);
        var followUps = await db.Set<FollowUp>().IgnoreQueryFilters()
            .CountAsync(f => EF.Property<Jobbliggaren.Domain.Applications.ApplicationId>(f, "ApplicationId") == appIdVo, ct);
        var notes = await db.Set<ApplicationNote>().IgnoreQueryFilters()
            .CountAsync(n => EF.Property<Jobbliggaren.Domain.Applications.ApplicationId>(n, "ApplicationId") == appIdVo, ct);
        var statusChanges = await db.Set<StatusChange>().IgnoreQueryFilters()
            .CountAsync(s => EF.Property<Jobbliggaren.Domain.Applications.ApplicationId>(s, "ApplicationId") == appIdVo, ct);
        return (followUps, notes, statusChanges);
    }

    [Fact]
    public async Task DELETE_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync($"/api/v1/applications/{Guid.NewGuid()}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DELETE_unknown_application_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var response = await client.DeleteAsync($"/api/v1/applications/{Guid.NewGuid()}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DELETE_own_application_returns_204_and_is_gone_from_list_and_detail()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var appId = await CreateManualApplicationAsync(client, ct);

        var response = await client.DeleteAsync($"/api/v1/applications/{appId}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Gone from detail — the row is physically removed (hard delete), so it is
        // absent, not merely hidden by the soft-delete query filter.
        var detail = await client.GetAsync($"/api/v1/applications/{appId}", ct);
        detail.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Gone from the list.
        var list = await client.GetAsync("/api/v1/applications/", ct);
        var json = await list.Content.ReadFromJsonAsync<JsonElement>(ct);
        var ids = json.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString());
        ids.ShouldNotContain(appId);
    }

    [Fact]
    public async Task DELETE_own_application_hard_removes_root_and_children_and_writes_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var appId = await CreateManualApplicationAsync(client, ct);
        var appGuid = Guid.Parse(appId);

        // Seed one row into each child table: a StatusChange (transition), a Note, and a FollowUp.
        (await client.PostAsJsonAsync($"/api/v1/applications/{appId}/transition",
            new { targetStatus = "Submitted" }, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/api/v1/applications/{appId}/notes",
            new { content = "En anteckning" }, ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PostAsJsonAsync($"/api/v1/applications/{appId}/follow-ups/log",
            new { note = "Ringde arbetsgivaren" }, ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Prove the shadow-FK predicate MATCHES the seeded child rows BEFORE the
        // delete, so the post-delete == 0 assertions below cannot pass vacuously
        // (e.g. if the strongly-typed VO predicate silently matched nothing) —
        // "1+ before -> 0 after" is what actually proves the cascade (test-writer Gap 1).
        using (var preScope = _factory.Services.CreateScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (fu, no, sc) = await ChildCountsAsync(preDb, appGuid, ct);
            fu.ShouldBeGreaterThan(0);
            no.ShouldBeGreaterThan(0);
            sc.ShouldBeGreaterThan(0);
        }

        var response = await client.DeleteAsync($"/api/v1/applications/{appId}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // HARD: the root row is physically gone (not merely soft-deleted) — even
        // IgnoreQueryFilters cannot retrieve it.
        var appIdVo = new Jobbliggaren.Domain.Applications.ApplicationId(appGuid);
        (await db.Applications.IgnoreQueryFilters().AnyAsync(a => a.Id == appIdVo, ct))
            .ShouldBeFalse();

        // The three child tables are physically emptied by the DB FK cascade.
        var (fuAfter, noAfter, scAfter) = await ChildCountsAsync(db, appGuid, ct);
        fuAfter.ShouldBe(0);
        noAfter.ShouldBe(0);
        scAfter.ShouldBe(0);

        // The audit trail survives the hard delete (Art. 5(2) accountability).
        (await db.AuditLogEntries.AsNoTracking()
            .AnyAsync(a => a.EventType == "Application.Deleted" && a.AggregateId == appGuid, ct))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task DELETE_other_users_application_returns_404_and_does_not_delete()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await AuthenticatedClientAsync(ct);
        var appId = await CreateManualApplicationAsync(owner, ct);

        var attacker = await AuthenticatedClientAsync(ct);
        var response = await attacker.DeleteAsync($"/api/v1/applications/{appId}", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner can still see it — the cross-user attempt did not delete it.
        var detail = await owner.GetAsync($"/api/v1/applications/{appId}", ct);
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
