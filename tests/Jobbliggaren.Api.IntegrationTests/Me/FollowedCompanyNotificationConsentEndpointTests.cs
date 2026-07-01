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
/// ADR 0087 D3/D5 (#311 PR-2b) — the company-follow notification consent endpoint
/// <c>PUT /api/v1/me/followed-company-notification-consent</c>, end-to-end on the wired API. Proves
/// the full loop through REAL Postgres: the consent write persists into the <c>preferences</c> jsonb
/// (EF <c>OwnsOne(...).ToJson()</c>) and a fresh <c>GET /profile</c> reads it back through the
/// projection — BY NAME on the wire (<c>followedCompanyNotificationsEnabled</c>, the contract the
/// settings follow-consent toggle reads). The Domain consent-stamping invariants are pinned at the
/// unit level (UpdateFollowedCompanyNotificationConsentCommandHandlerTests + PR-4's
/// JobSeekerFollowedCompanyConsentTests); this file pins the WIRE: auth gate, status codes, the
/// jsonb round-trip, the Art. 7 evidence persisting to the row, and the audit row.
/// <para>
/// <b>Audit gate (ADR 0022):</b> a consent change is accountability-relevant, so the command is
/// <c>IAuditableCommand</c> and the pipeline's <c>AuditBehavior</c> must write exactly one
/// <c>audit_log</c> row (EventType <c>JobSeeker.FollowedCompanyNotificationConsentUpdated</c>,
/// AggregateType <c>JobSeeker</c>, AggregateId = the JobSeeker's Id, UserId = the actor). A failure
/// path (anonymous) writes NO row. Mirrors <see cref="NotificationConsentEndpointTests"/>.
/// </para>
/// <para>
/// <b>Separateness (ADR 0087 D5):</b> the two consents are distinct Art. 6/7 purposes — enabling
/// the follow consent must NOT flip the background-match flag on the wire.
/// </para>
/// </summary>
[Collection("Api")]
public class FollowedCompanyNotificationConsentEndpointTests(ApiFactory factory)
{
    private const string ConsentPath = "/api/v1/me/followed-company-notification-consent";

    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);

        // The user-id behind the session — needed for the direct-row Art. 7 evidence check.
        var me = await _client.GetFromJsonAsync<JsonElement>("/api/v1/me", ct);
        return Guid.Parse(me.GetProperty("userId").GetString()!);
    }

    private async Task<JsonElement> GetProfileAsync(CancellationToken ct)
    {
        var response = await _client.GetAsync("/api/v1/me/profile", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    // The audit AggregateId is the JobSeeker's Id (the handler echoes it, not the user-id).
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
    public async Task PUT_follow_consent_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // No Authorization header → RequireAuthorization rejects before the handler runs.
        var response = await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_follow_consent_authed_enable_returns_204_and_profile_reflects_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // A fresh seeker projects the opt-in default: follow-notifications OFF.
        var before = await GetProfileAsync(ct);
        before.GetProperty("followedCompanyNotificationsEnabled").GetBoolean().ShouldBeFalse();

        var put = await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Reads back through real Postgres + the projection, by NAME on the wire.
        var after = await GetProfileAsync(ct);
        after.GetProperty("followedCompanyNotificationsEnabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task PUT_follow_consent_disable_after_enable_flips_profile_to_false()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await GetProfileAsync(ct);
        after.GetProperty("followedCompanyNotificationsEnabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task PUT_follow_consent_enable_does_not_flip_background_match_on_the_wire()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var put = await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // ADR 0087 D5 — separate purposes: enabling follow must not enable background-match.
        var after = await GetProfileAsync(ct);
        after.GetProperty("followedCompanyNotificationsEnabled").GetBoolean().ShouldBeTrue();
        after.GetProperty("backgroundMatchNotificationsEnabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task PUT_follow_consent_enable_persists_consent_timestamp_to_the_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);

        var put = await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Art. 7(1) evidence — the consent timestamp is persisted into the preferences jsonb (the
        // DTO deliberately does NOT project it, so verify directly against the row).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.AsNoTracking()
            .SingleAsync(js => js.UserId == userId, ct);

        seeker.Preferences.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        seeker.Preferences.FollowedCompanyNotificationConsentAt.ShouldNotBeNull();
        seeker.Preferences.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
    }

    // ADR 0022 — a successful consent change writes exactly one audit_log row via the pipeline's
    // AuditBehavior. AggregateId = the JobSeeker's Id (the echoed Result value), UserId = the actor.
    [Fact]
    public async Task PUT_follow_consent_on_success_writes_one_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        var jobSeekerId = await ReadJobSeekerIdAsync(userId, ct);

        // Registration may already have audited a JobSeeker mutation against this aggregate;
        // assert the DELTA from the consent PUT, not an absolute count of one.
        var before = await ReadAuditEntriesAsync(jobSeekerId, ct);

        var put = await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await ReadAuditEntriesAsync(jobSeekerId, ct);
        var consentRows = after
            .Where(a => a.EventType == "JobSeeker.FollowedCompanyNotificationConsentUpdated")
            .ToList();

        consentRows.Count.ShouldBe(1, "exakt en follow-consent-audit-rad ska skrivas av AuditBehavior");
        after.Count.ShouldBe(before.Count + 1, "endast consent-PUT:en lägger till en rad");

        var entry = consentRows[0];
        entry.AggregateType.ShouldBe("JobSeeker");
        entry.AggregateId.ShouldBe(jobSeekerId);
        entry.UserId.ShouldBe(userId);
    }

    // Failure path (anonymous) — no audit row is written (RequireAuthorization rejects before the
    // pipeline runs). Cheap total-count delta on top of the 401 already asserted above.
    [Fact]
    public async Task PUT_follow_consent_anonymous_writes_no_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = await ReadAuditEntryCountAsync(ct);

        var response = await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await ReadAuditEntryCountAsync(ct)).ShouldBe(before);
    }
}
