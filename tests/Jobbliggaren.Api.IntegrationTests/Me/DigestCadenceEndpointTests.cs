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
/// ADR 0087 D2 — <c>PUT /api/v1/me/digest-cadence</c>, end-to-end on the wired API and REAL
/// Postgres. The cadence is shared by the two notification consents and owned by neither, so the
/// load-bearing pins here are that a cadence write leaves both consents' flags and Art. 7
/// timestamps exactly as they were read from the row — the sequence that, while the cadence rode
/// the background-match consent's full-replace contract, re-granted a withdrawn consent — and that
/// it is audited under its own event name, never as a consent act.
/// </summary>
[Collection("Api")]
public class DigestCadenceEndpointTests(ApiFactory factory)
{
    private const string CadencePath = "/api/v1/me/digest-cadence";
    private const string ConsentPath = "/api/v1/me/background-match-notification-consent";

    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);

        var me = await _client.GetFromJsonAsync<JsonElement>("/api/v1/me", ct);
        return Guid.Parse(me.GetProperty("userId").GetString()!);
    }

    private async Task<JobSeeker> ReadSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, ct);
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

    [Fact]
    public async Task PUT_digest_cadence_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PutAsJsonAsync(CadencePath, new { cadence = "Daily" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_digest_cadence_round_trips_by_name_on_the_wire()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var put = await _client.PutAsJsonAsync(CadencePath, new { cadence = "Daily" }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = await _client.GetFromJsonAsync<JsonElement>("/api/v1/me/profile", ct);
        // The wire form is the NAME, not the ordinal — JsonStringEnumConverter contract.
        profile.GetProperty("digestCadence").ValueKind.ShouldBe(JsonValueKind.String);
        profile.GetProperty("digestCadence").GetString().ShouldBe("Daily");
    }

    [Fact]
    public async Task PUT_digest_cadence_with_an_unknown_name_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var put = await _client.PutAsJsonAsync(CadencePath, new { cadence = "Hourly" }, ct);

        put.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PUT_digest_cadence_without_cadence_returns_400_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(CadencePath, new { cadence = "Daily" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var put = await _client.PutAsJsonAsync(CadencePath, new { }, ct);

        put.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadSeekerAsync(userId, ct)).Preferences.DigestCadence.ShouldBe(DigestCadence.Daily);
    }

    // The defect's own sequence, end to end: consent given, then withdrawn; the row is read; then a
    // cadence change. The withdrawal and both Art. 7 timestamps must be exactly as read.
    [Fact]
    public async Task PUT_digest_cadence_after_withdrawal_leaves_consent_flag_and_both_Art7_timestamps_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var withdrawn = (await ReadSeekerAsync(userId, ct)).Preferences;
        withdrawn.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        withdrawn.NotificationConsentAt.ShouldNotBeNull();
        withdrawn.NotificationConsentWithdrawnAt.ShouldNotBeNull();

        var put = await _client.PutAsJsonAsync(CadencePath, new { cadence = "Daily" }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = (await ReadSeekerAsync(userId, ct)).Preferences;
        after.DigestCadence.ShouldBe(DigestCadence.Daily);
        after.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        after.NotificationConsentAt.ShouldBe(withdrawn.NotificationConsentAt);
        after.NotificationConsentWithdrawnAt.ShouldBe(withdrawn.NotificationConsentWithdrawnAt);
    }

    [Fact]
    public async Task PUT_digest_cadence_while_consented_leaves_consent_flag_and_both_Art7_timestamps_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var consented = (await ReadSeekerAsync(userId, ct)).Preferences;

        var put = await _client.PutAsJsonAsync(CadencePath, new { cadence = "Daily" }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = (await ReadSeekerAsync(userId, ct)).Preferences;
        after.DigestCadence.ShouldBe(DigestCadence.Daily);
        after.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        after.NotificationConsentAt.ShouldBe(consented.NotificationConsentAt);
        after.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    // The cadence contract carries no consent value: a stray `enabled` is not bound, so a cadence
    // request cannot give (or re-give) a consent.
    [Fact]
    public async Task PUT_digest_cadence_with_a_stray_enabled_leaves_the_consent_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);

        var put = await _client.PutAsJsonAsync(CadencePath, new { cadence = "Daily", enabled = true }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = (await ReadSeekerAsync(userId, ct)).Preferences;
        after.DigestCadence.ShouldBe(DigestCadence.Daily);
        after.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        after.NotificationConsentAt.ShouldBeNull();
    }

    // A cadence save is audited under its own name with no payload, and is never recorded as a
    // consent act.
    [Fact]
    public async Task PUT_digest_cadence_writes_one_DigestCadenceUpdated_audit_row_and_no_consent_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        var jobSeekerId = (await ReadSeekerAsync(userId, ct)).Id.Value;
        var before = (await ReadAuditEntriesAsync(jobSeekerId, ct)).Select(a => a.Id).ToHashSet();

        var put = await _client.PutAsJsonAsync(CadencePath, new { cadence = "Daily" }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var added = (await ReadAuditEntriesAsync(jobSeekerId, ct)).Where(a => !before.Contains(a.Id)).ToList();
        added.Count.ShouldBe(1);
        added[0].EventType.ShouldBe("JobSeeker.DigestCadenceUpdated");
        added[0].AggregateType.ShouldBe("JobSeeker");
        added[0].UserId.ShouldBe(userId);
        added[0].Payload.ShouldBeNull();
        added.ShouldNotContain(a => a.EventType == "JobSeeker.NotificationConsentUpdated");
    }
}
