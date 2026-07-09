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

/// <summary>
/// #630 PR 9 — POST /api/v1/applications/batch-transition (CTO-bound Variant A:
/// all-or-nothing two-phase, per-item targets, empty 200). Runs against real
/// Postgres — this suite is the translation oracle for the per-id equality
/// lookups (a strongly-typed-ID Contains() would compile but fail Npgsql
/// translation at runtime; InMemory hides that class of bug).
/// </summary>
[Collection("Api")]
public class BatchTransitionEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static readonly object CreateBody = new { jobAdId = (Guid?)null, coverLetter = (string?)null };

    private async Task<HttpClient> RegisterUserAsync(string userPrefix, CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"{userPrefix}-{Guid.NewGuid():N}@jobbliggaren.test";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private static async Task<Guid> CreateApplicationAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return Guid.Parse(json.GetProperty("id").GetString()!);
    }

    private static object Body(params (Guid Id, string Target)[] items) => new
    {
        items = items.Select(i => new { applicationId = i.Id, targetStatus = i.Target }).ToArray(),
    };

    private async Task<Domain.Applications.Application> ReadApplicationAsync(
        Guid id, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appId = new Domain.Applications.ApplicationId(id);
        return await db.Applications
            .AsNoTracking()
            .Include(a => a.StatusChanges)
            .SingleAsync(a => a.Id == appId, ct);
    }

    private async Task<List<Jobbliggaren.Domain.Auditing.AuditLogEntry>> ReadAuditEntriesAsync(
        Guid aggregateId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries
            .Where(a => a.AggregateId == aggregateId)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(ct);
    }

    // ---------------------------------------------------------------
    // Auth + happy path
    // ---------------------------------------------------------------

    [Fact]
    public async Task BatchTransition_WithoutAuth_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((Guid.NewGuid(), "Submitted")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BatchTransition_OwnApplications_Returns200AndPersistsAllStatuses()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-happy", ct);
        var id1 = await CreateApplicationAsync(client, ct);
        var id2 = await CreateApplicationAsync(client, ct);
        var id3 = await CreateApplicationAsync(client, ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((id1, "Submitted"), (id2, "Submitted"), (id3, "Ghosted")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadApplicationAsync(id1, ct)).Status.ShouldBe(ApplicationStatus.Submitted);
        (await ReadApplicationAsync(id2, ct)).Status.ShouldBe(ApplicationStatus.Submitted);
        (await ReadApplicationAsync(id3, ct)).Status.ShouldBe(ApplicationStatus.Ghosted);
    }

    [Fact]
    public async Task BatchTransition_PersistsOneStatusChangeTimelineRowPerApplication()
    {
        // ADR 0092 D4: the timeline row is appended inside TransitionTo per
        // item and persists in the same unit of work.
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-timeline", ct);
        var id1 = await CreateApplicationAsync(client, ct);
        var id2 = await CreateApplicationAsync(client, ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((id1, "Submitted"), (id2, "Rejected")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var app1 = await ReadApplicationAsync(id1, ct);
        var change1 = app1.StatusChanges.ShouldHaveSingleItem();
        change1.From.ShouldBe(ApplicationStatus.Draft);
        change1.To.ShouldBe(ApplicationStatus.Submitted);

        var app2 = await ReadApplicationAsync(id2, ct);
        var change2 = app2.StatusChanges.ShouldHaveSingleItem();
        change2.From.ShouldBe(ApplicationStatus.Draft);
        change2.To.ShouldBe(ApplicationStatus.Rejected);
    }

    [Fact]
    public async Task BatchTransition_ToSubmitted_StampsAppliedAtEndToEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-applied", ct);
        var id = await CreateApplicationAsync(client, ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((id, "Submitted")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadApplicationAsync(id, ct)).AppliedAt.ShouldNotBeNull();
    }

    // ---------------------------------------------------------------
    // All-or-nothing + IDOR
    // ---------------------------------------------------------------

    [Fact]
    public async Task BatchTransition_WithUnknownId_Returns404AndPersistsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-unknown", ct);
        var ownId = await CreateApplicationAsync(client, ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((ownId, "Submitted"), (Guid.NewGuid(), "Submitted")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var own = await ReadApplicationAsync(ownId, ct);
        own.Status.ShouldBe(ApplicationStatus.Draft);
        own.StatusChanges.ShouldBeEmpty();
    }

    [Fact]
    public async Task BatchTransition_CrossUserApplication_Returns404AndPersistsNothing()
    {
        // IDOR parity with the single endpoint: cross-user and unknown ids are
        // indistinguishable (uniform 404, no enumeration oracle), and the
        // victim's aggregate — and the caller's own item in the same batch —
        // stay untouched (all-or-nothing).
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("batch-victim", ct);
        var clientB = await RegisterUserAsync("batch-attacker", ct);
        var victimId = await CreateApplicationAsync(clientA, ct);
        var ownId = await CreateApplicationAsync(clientB, ct);

        var response = await clientB.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((ownId, "Submitted"), (victimId, "Submitted")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadApplicationAsync(victimId, ct)).Status.ShouldBe(ApplicationStatus.Draft);
        var own = await ReadApplicationAsync(ownId, ct);
        own.Status.ShouldBe(ApplicationStatus.Draft);
        own.StatusChanges.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // Validation
    // ---------------------------------------------------------------

    [Fact]
    public async Task BatchTransition_UnknownStatus_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-badstatus", ct);
        var id = await CreateApplicationAsync(client, ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((id, "NotAStatus")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BatchTransition_EmptyItems_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-empty", ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            new { items = Array.Empty<object>() },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BatchTransition_OverMaxItems_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-cap", ct);
        var items = Enumerable.Range(0, 101)
            .Select(_ => (Guid.NewGuid(), "Submitted"))
            .ToArray();

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body(items),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BatchTransition_ConflictingDuplicates_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-conflict", ct);
        var id = await CreateApplicationAsync(client, ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((id, "Submitted"), (id, "Rejected")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadApplicationAsync(id, ct)).Status.ShouldBe(ApplicationStatus.Draft);
    }

    // ---------------------------------------------------------------
    // Audit (ADR 0022 — one row per application, batch-grouped)
    // ---------------------------------------------------------------

    [Fact]
    public async Task BatchTransition_OnSuccess_WritesOneAuditEntryPerApplication()
    {
        // A1 audit bind: same EventType as the single transition, so
        // per-aggregate audit queries see batch and single moves identically;
        // the shared correlation id groups the rows as one batch.
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-audit", ct);
        var id1 = await CreateApplicationAsync(client, ct);
        var id2 = await CreateApplicationAsync(client, ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((id1, "Submitted"), (id2, "Rejected")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var entries1 = (await ReadAuditEntriesAsync(id1, ct))
            .Where(e => e.EventType == "Application.StatusTransitioned")
            .ToList();
        var entries2 = (await ReadAuditEntriesAsync(id2, ct))
            .Where(e => e.EventType == "Application.StatusTransitioned")
            .ToList();

        var entry1 = entries1.ShouldHaveSingleItem();
        var entry2 = entries2.ShouldHaveSingleItem();
        entry1.AggregateType.ShouldBe("Application");
        entry2.AggregateType.ShouldBe("Application");
        entry1.CorrelationId.ShouldBe(entry2.CorrelationId);
        entry1.OccurredAt.ShouldBe(entry2.OccurredAt);
    }

    [Fact]
    public async Task BatchTransition_On404_WritesNoAuditEntries()
    {
        // The batch aborts before any mutation; ADR 0022 audits success only —
        // no rows for the caller's own (untouched) item either.
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("batch-audit404", ct);
        var ownId = await CreateApplicationAsync(client, ct);

        var response = await client.PostAsJsonAsync(
            "/api/v1/applications/batch-transition",
            Body((ownId, "Submitted"), (Guid.NewGuid(), "Submitted")),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var entries = (await ReadAuditEntriesAsync(ownId, ct))
            .Where(e => e.EventType == "Application.StatusTransitioned")
            .ToList();
        entries.ShouldBeEmpty();
    }
}
