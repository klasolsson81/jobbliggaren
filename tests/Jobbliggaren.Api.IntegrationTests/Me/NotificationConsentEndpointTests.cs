using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.MyProfile;

/// <summary>
/// ADR 0080 Vag 4 PR-6 — the background-match notification consent endpoint
/// <c>PUT /api/v1/me/notification-consent</c>, end-to-end on the wired API. Proves the full
/// loop through REAL Postgres: the consent write persists into the <c>preferences</c> jsonb
/// (EF <c>OwnsOne(...).ToJson()</c>) and a fresh <c>GET /profile</c> reads it back through the
/// projection — BY NAME on the wire (<c>backgroundMatchNotificationsEnabled</c> +
/// <c>digestCadence</c>, the contract the settings page reads). The Domain consent-stamping
/// invariants are pinned at the unit level (UpdateNotificationConsentCommandHandlerTests) and
/// the owned-JSON back-compat at PreferencesConsentBackcompatTests; this file pins the WIRE:
/// auth gate, status codes, the jsonb round-trip, and the Art. 7 evidence persisting to the row.
/// <para>
/// <b>Audit gate (security-auditor Major, ADR 0022):</b> a consent change is accountability-
/// relevant, so the command is <c>IAuditableCommand</c> and the pipeline's <c>AuditBehavior</c>
/// must write exactly one <c>audit_log</c> row (EventType <c>JobSeeker.NotificationConsentUpdated</c>,
/// AggregateType <c>JobSeeker</c>, AggregateId = the JobSeeker's Id, UserId = the actor). This is a
/// pipeline concern, so it can only be proven where the behavior actually runs — here. A failure
/// path (anonymous) writes NO row. Mirrors the audit-parity pattern in
/// <see cref="Jobbliggaren.Api.IntegrationTests.Auditing.AuditLogIntegrationTests"/>.
/// </para>
/// <para>
/// Auth pattern mirrors <see cref="Jobbliggaren.Api.IntegrationTests.Matching.MeMatchCountEndpointTests"/>
/// (<see cref="AuthTestHelpers.RegisterAndGetSessionIdAsync"/> → Bearer session-id; register
/// also provisions the JobSeeker, so GET /profile returns 200, not 404).
/// </para>
/// </summary>
[Collection("Api")]
public class NotificationConsentEndpointTests(ApiFactory factory)
{
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
    public async Task PUT_notification_consent_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // No Authorization header → RequireAuthorization rejects before the handler runs.
        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/notification-consent",
            new { enabled = true, cadence = "Weekly" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_notification_consent_authed_enable_returns_204_and_profile_reflects_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // A fresh seeker projects the opt-in default: OFF / Weekly.
        var before = await GetProfileAsync(ct);
        before.GetProperty("backgroundMatchNotificationsEnabled").GetBoolean().ShouldBeFalse();
        before.GetProperty("digestCadence").GetString().ShouldBe("Weekly");

        var put = await _client.PutAsJsonAsync(
            "/api/v1/me/notification-consent",
            new { enabled = true, cadence = "Weekly" },
            ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Reads back through real Postgres + the projection, by NAME on the wire.
        var after = await GetProfileAsync(ct);
        after.GetProperty("backgroundMatchNotificationsEnabled").GetBoolean().ShouldBeTrue();
        after.GetProperty("digestCadence").GetString().ShouldBe("Weekly");
    }

    [Fact]
    public async Task PUT_notification_consent_cadence_serializes_by_name_on_the_wire()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var put = await _client.PutAsJsonAsync(
            "/api/v1/me/notification-consent",
            new { enabled = true, cadence = "Daily" },
            ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await GetProfileAsync(ct);
        // The wire form is the NAME, not the ordinal — JsonStringEnumConverter contract.
        after.GetProperty("digestCadence").ValueKind.ShouldBe(JsonValueKind.String);
        after.GetProperty("digestCadence").GetString().ShouldBe("Daily");
    }

    [Fact]
    public async Task PUT_notification_consent_disable_after_enable_flips_profile_to_false()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Enable, then disable — the projection must reflect the final OFF state.
        (await _client.PutAsJsonAsync(
            "/api/v1/me/notification-consent",
            new { enabled = true, cadence = "Weekly" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _client.PutAsJsonAsync(
            "/api/v1/me/notification-consent",
            new { enabled = false, cadence = "Weekly" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await GetProfileAsync(ct);
        after.GetProperty("backgroundMatchNotificationsEnabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task PUT_notification_consent_enable_persists_consent_timestamp_to_the_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);

        var put = await _client.PutAsJsonAsync(
            "/api/v1/me/notification-consent",
            new { enabled = true, cadence = "Weekly" },
            ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Art. 7(1) evidence — the consent timestamp is persisted into the preferences jsonb
        // (the DTO deliberately does NOT project it, so verify directly against the row).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.AsNoTracking()
            .SingleAsync(js => js.UserId == userId, ct);

        seeker.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        seeker.Preferences.DigestCadence.ShouldBe(DigestCadence.Weekly);
        seeker.Preferences.NotificationConsentAt.ShouldNotBeNull();
        seeker.Preferences.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    // security-auditor Major (ADR 0022) — a successful consent change writes exactly one audit_log
    // row via the pipeline's AuditBehavior. AggregateId = the JobSeeker's Id (the echoed Result
    // value), UserId = the actor. Pins the grade gate: if the row does NOT appear (behavior
    // ordering / ExtractAggregateId), this fails — it is the only place the behavior runs.
    [Fact]
    public async Task PUT_notification_consent_on_success_writes_one_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        var jobSeekerId = await ReadJobSeekerIdAsync(userId, ct);

        // Registration may already have audited a JobSeeker mutation against this aggregate;
        // assert the DELTA from the consent PUT, not an absolute count of one.
        var before = await ReadAuditEntriesAsync(jobSeekerId, ct);

        var put = await _client.PutAsJsonAsync(
            "/api/v1/me/notification-consent",
            new { enabled = true, cadence = "Weekly" },
            ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await ReadAuditEntriesAsync(jobSeekerId, ct);
        var consentRows = after
            .Where(a => a.EventType == "JobSeeker.NotificationConsentUpdated")
            .ToList();

        consentRows.Count.ShouldBe(1, "exakt en consent-audit-rad ska skrivas av AuditBehavior");
        after.Count.ShouldBe(before.Count + 1, "endast consent-PUT:en lägger till en rad");

        var entry = consentRows[0];
        entry.AggregateType.ShouldBe("JobSeeker");
        entry.AggregateId.ShouldBe(jobSeekerId);
        entry.UserId.ShouldBe(userId);
    }

    // Failure path (anonymous) — no audit row is written (RequireAuthorization rejects before the
    // pipeline runs). Cheap total-count delta on top of the 401 already asserted above.
    [Fact]
    public async Task PUT_notification_consent_anonymous_writes_no_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = await ReadAuditEntryCountAsync(ct);

        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/notification-consent",
            new { enabled = true, cadence = "Weekly" },
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await ReadAuditEntryCountAsync(ct)).ShouldBe(before);
    }
}
