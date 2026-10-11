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
/// ADR 0164 (#2107) — <c>PUT /api/v1/me/notification-mail</c>, the switch for every notification mail,
/// end to end on the wired API and real Postgres (security-auditor V4). The switch is a pause inside the
/// consents, so the load-bearing pins are that a write in either direction leaves both consents and the
/// two mail-type switches exactly as they were (V3), and that it is audited under its own name with the
/// requested <c>enabled</c> only (V11).
/// </summary>
[Collection("Api")]
public class NotificationMailEndpointTests(ApiFactory factory)
{
    private const string MailPath = "/api/v1/me/notification-mail";
    private const string TopMatchMailPath = "/api/v1/me/top-match-mail";
    private const string ConsentPath = "/api/v1/me/background-match-notification-consent";
    private const string FollowPath = "/api/v1/me/followed-company-notification-consent";
    private const string EventType = "JobSeeker.NotificationMailUpdated";

    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var me = await _client.GetFromJsonAsync<JsonElement>("/api/v1/me", ct);
        return Guid.Parse(me.GetProperty("userId").GetString()!);
    }

    private async Task<JsonElement> GetProfileAsync(CancellationToken ct)
    {
        var response = await _client.GetAsync("/api/v1/me/profile", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
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

    private async Task<int> ReadAuditEntryCountAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries.CountAsync(ct);
    }

    [Fact]
    public async Task PUT_notification_mail_anonymous_returns_401_and_writes_no_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = await ReadAuditEntryCountAsync(ct);

        var response = await _client.PutAsJsonAsync(MailPath, new { enabled = false }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadAuditEntryCountAsync(ct)).ShouldBe(before);
    }

    [Fact]
    public async Task PUT_notification_mail_returns_204_and_profile_reads_the_new_value_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await GetProfileAsync(ct)).GetProperty("notificationMailEnabled").GetBoolean().ShouldBeTrue();

        (await _client.PutAsJsonAsync(MailPath, new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetProfileAsync(ct)).GetProperty("notificationMailEnabled").GetBoolean().ShouldBeFalse();
        (await ReadSeekerAsync(userId, ct)).Preferences.NotificationMailOffAt.ShouldNotBeNull();

        (await _client.PutAsJsonAsync(MailPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetProfileAsync(ct)).GetProperty("notificationMailEnabled").GetBoolean().ShouldBeTrue();
        (await ReadSeekerAsync(userId, ct)).Preferences.NotificationMailOffAt.ShouldBeNull();
    }

    // The profile carries the switch as a bool and never its stamp (security-auditor, row-14
    // continuation: the stamps leave the aggregate only as derived booleans).
    [Fact]
    public async Task GET_profile_after_a_pause_carries_no_pause_stamp()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(MailPath, new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = await GetProfileAsync(ct);

        profile.EnumerateObject().Select(property => property.Name)
            .ShouldNotContain(name => name.EndsWith("OffAt", StringComparison.OrdinalIgnoreCase));
    }

    // A missing `enabled` is refused, never bound to `false`: an empty body must not pause anything.
    [Fact]
    public async Task PUT_notification_mail_without_enabled_returns_400_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        var jobSeekerId = (await ReadSeekerAsync(userId, ct)).Id.Value;
        var auditBefore = (await ReadAuditEntriesAsync(jobSeekerId, ct)).Count;

        var put = await _client.PutAsJsonAsync(MailPath, new { }, ct);

        put.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadSeekerAsync(userId, ct)).Preferences.NotificationMailOffAt.ShouldBeNull();
        (await ReadAuditEntriesAsync(jobSeekerId, ct)).Count.ShouldBe(auditBefore);
    }

    [Fact]
    public async Task PUT_notification_mail_writes_exactly_one_audit_row_carrying_only_the_requested_enabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        var jobSeekerId = (await ReadSeekerAsync(userId, ct)).Id.Value;
        var before = (await ReadAuditEntriesAsync(jobSeekerId, ct)).Select(a => a.Id).ToHashSet();

        (await _client.PutAsJsonAsync(MailPath, new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var added = (await ReadAuditEntriesAsync(jobSeekerId, ct)).Where(a => !before.Contains(a.Id)).ToList();
        var entry = added.ShouldHaveSingleItem();
        entry.EventType.ShouldBe(EventType);
        entry.AggregateType.ShouldBe("JobSeeker");
        entry.AggregateId.ShouldBe(jobSeekerId);
        entry.UserId.ShouldBe(userId);
        using var payload = JsonDocument.Parse(entry.Payload.ShouldNotBeNull());
        var property = payload.RootElement.EnumerateObject().ShouldHaveSingleItem();
        property.Name.ShouldBe("enabled");
        property.Value.ValueKind.ShouldBe(JsonValueKind.False);
    }

    // security-auditor V3: neither direction is a consent act. Both consents are granted first, and the
    // background-match consent is then withdrawn, so a write that touched either would show.
    [Fact]
    public async Task PUT_notification_mail_in_either_direction_leaves_both_consents_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.PutAsJsonAsync(FollowPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var seeker = await ReadSeekerAsync(userId, ct);
        var consents = seeker.Preferences;
        var jobSeekerId = seeker.Id.Value;
        var auditBefore = (await ReadAuditEntriesAsync(jobSeekerId, ct)).Select(a => a.Id).ToHashSet();

        foreach (var enabled in new[] { false, true })
        {
            (await _client.PutAsJsonAsync(MailPath, new { enabled }, ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var profile = await GetProfileAsync(ct);
            profile.GetProperty("backgroundMatchNotificationsEnabled").GetBoolean().ShouldBeFalse();
            profile.GetProperty("followedCompanyNotificationsEnabled").GetBoolean().ShouldBeTrue();
            var stored = (await ReadSeekerAsync(userId, ct)).Preferences;
            stored.ShouldBe(consents with { NotificationMailOffAt = stored.NotificationMailOffAt });
        }

        (await ReadAuditEntriesAsync(jobSeekerId, ct))
            .Where(a => !auditBefore.Contains(a.Id))
            .Select(a => a.EventType)
            .ToList()
            .ShouldBe([EventType, EventType]);
    }

    // Switching every mail on lifts only the pause: a mail type the user switched off stays off.
    [Fact]
    public async Task PUT_notification_mail_on_leaves_a_switched_off_mail_type_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(TopMatchMailPath, new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.PutAsJsonAsync(MailPath, new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var topMatchOffAt = (await ReadSeekerAsync(userId, ct)).Preferences.TopMatchMailOffAt;
        topMatchOffAt.ShouldNotBeNull();

        (await _client.PutAsJsonAsync(MailPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var profile = await GetProfileAsync(ct);
        profile.GetProperty("notificationMailEnabled").GetBoolean().ShouldBeTrue();
        profile.GetProperty("topMatchMailEnabled").GetBoolean().ShouldBeFalse();
        profile.GetProperty("matchSummaryMailEnabled").GetBoolean().ShouldBeTrue();
        (await ReadSeekerAsync(userId, ct)).Preferences.TopMatchMailOffAt.ShouldBe(topMatchOffAt);
    }
}
