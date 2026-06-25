using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.MyProfile;

/// <summary>
/// #192 (GDPR Art. 5(2)/30) — the profile-update endpoint <c>PATCH /api/v1/me/profile</c> must
/// write exactly ONE <c>audit_log</c> row on success (the owner-scoped JobSeeker mutation is
/// accountability-relevant; DisplayName is PII). The opt-in <c>AuditBehavior</c> only runs in the
/// real pipeline, so this can only be proven end-to-end on the wired API against REAL Postgres —
/// mirrors <see cref="NotificationConsentEndpointTests"/>. The row carries EventType
/// <c>JobSeeker.ProfileUpdated</c>, AggregateType <c>JobSeeker</c>, AggregateId = the JobSeeker's
/// Id (the handler echoes it via <c>Result&lt;Guid&gt;</c>, not the user-id), UserId = the actor.
/// A failure path (anonymous) writes NO row. Asserts the DELTA (registration itself may audit).
/// </summary>
[Collection("Api")]
public class UpdateMyProfileAuditTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
        var me = await _client.GetFromJsonAsync<JsonElement>("/api/v1/me", ct);
        return Guid.Parse(me.GetProperty("userId").GetString()!);
    }

    private async Task<Guid> ReadJobSeekerIdAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, ct);
        return seeker.Id.Value;
    }

    private async Task<List<AuditLogEntry>> ReadAuditEntriesAsync(Guid aggregateId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries
            .Where(a => a.AggregateId == aggregateId)
            .OrderBy(a => a.OccurredAt)
            .ToListAsync(ct);
    }

    private async Task<int> ReadAuditEntryCountAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries.CountAsync(ct);
    }

    [Fact]
    public async Task Patch_profile_on_success_writes_one_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        var jobSeekerId = await ReadJobSeekerIdAsync(userId, ct);
        var before = await ReadAuditEntriesAsync(jobSeekerId, ct);

        var response = await _client.PatchAsJsonAsync(
            "/api/v1/me/profile", new { displayName = "Klas Ny", language = "en" }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await ReadAuditEntriesAsync(jobSeekerId, ct);
        after.Count.ShouldBe(before.Count + 1, "only the profile PATCH adds a row");

        // Filter by EventType + Single (parity NotificationConsentEndpointTests) — robust against
        // any OccurredAt tie-ordering, and proves exactly one ProfileUpdated row was written.
        var profileRows = after.Where(a => a.EventType == "JobSeeker.ProfileUpdated").ToList();
        profileRows.Count.ShouldBe(1);

        var row = profileRows[0];
        row.AggregateType.ShouldBe("JobSeeker");
        row.AggregateId.ShouldBe(jobSeekerId); // the JobSeeker id (echoed), NOT the user-id
        row.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task Patch_profile_with_blank_displayName_writes_no_audit_row()
    {
        // The domain-failure path (authenticated, blank display name → 400) DOES enter the
        // pipeline, unlike the anonymous 401 path — so this locks AuditBehavior's skip-on-failure
        // end-to-end: a Result.Failure<Guid> writes no audit row.
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        var jobSeekerId = await ReadJobSeekerIdAsync(userId, ct);
        var before = await ReadAuditEntriesAsync(jobSeekerId, ct);

        var response = await _client.PatchAsJsonAsync(
            "/api/v1/me/profile", new { displayName = "   ", language = "sv" }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var after = await ReadAuditEntriesAsync(jobSeekerId, ct);
        after.Count.ShouldBe(before.Count, "a failed (domain-invalid) profile update writes no audit row");
    }

    [Fact]
    public async Task Patch_profile_when_anonymous_writes_no_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = await ReadAuditEntryCountAsync(ct);

        // No Authorization header → the write policy rejects before the handler/audit runs.
        var response = await _client.PatchAsJsonAsync(
            "/api/v1/me/profile", new { displayName = "Anon", language = "sv" }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var after = await ReadAuditEntryCountAsync(ct);
        after.ShouldBe(before, "an unauthorized profile update must write no audit row");
    }
}
