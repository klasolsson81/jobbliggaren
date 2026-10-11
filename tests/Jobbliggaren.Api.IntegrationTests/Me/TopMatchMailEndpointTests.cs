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
/// ADR 0164 (#2107) — <c>PUT /api/v1/me/top-match-mail</c>, the Toppmatch mail switch, end to end on the
/// wired API and real Postgres (security-auditor V4). A write in either direction moves only its own
/// stamp: both consents, the all-mail switch and the summary switch stay as they were (V3).
/// </summary>
[Collection("Api")]
public class TopMatchMailEndpointTests(ApiFactory factory)
{
    private const string MailPath = "/api/v1/me/top-match-mail";
    private const string ConsentPath = "/api/v1/me/background-match-notification-consent";
    private const string FollowPath = "/api/v1/me/followed-company-notification-consent";
    private const string EventType = "JobSeeker.TopMatchMailUpdated";

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
    public async Task PUT_top_match_mail_anonymous_returns_401_and_writes_no_audit_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = await ReadAuditEntryCountAsync(ct);

        var response = await _client.PutAsJsonAsync(MailPath, new { enabled = false }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadAuditEntryCountAsync(ct)).ShouldBe(before);
    }

    [Fact]
    public async Task PUT_top_match_mail_returns_204_and_profile_reads_the_new_value_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await GetProfileAsync(ct)).GetProperty("topMatchMailEnabled").GetBoolean().ShouldBeTrue();

        (await _client.PutAsJsonAsync(MailPath, new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var off = await GetProfileAsync(ct);
        off.GetProperty("topMatchMailEnabled").GetBoolean().ShouldBeFalse();
        off.GetProperty("notificationMailEnabled").GetBoolean().ShouldBeTrue();
        off.GetProperty("matchSummaryMailEnabled").GetBoolean().ShouldBeTrue();
        (await ReadSeekerAsync(userId, ct)).Preferences.TopMatchMailOffAt.ShouldNotBeNull();

        (await _client.PutAsJsonAsync(MailPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetProfileAsync(ct)).GetProperty("topMatchMailEnabled").GetBoolean().ShouldBeTrue();
        (await ReadSeekerAsync(userId, ct)).Preferences.TopMatchMailOffAt.ShouldBeNull();
    }

    [Fact]
    public async Task PUT_top_match_mail_without_enabled_returns_400_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        var jobSeekerId = (await ReadSeekerAsync(userId, ct)).Id.Value;
        var auditBefore = (await ReadAuditEntriesAsync(jobSeekerId, ct)).Count;

        var put = await _client.PutAsJsonAsync(MailPath, new { }, ct);

        put.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadSeekerAsync(userId, ct)).Preferences.TopMatchMailOffAt.ShouldBeNull();
        (await ReadAuditEntriesAsync(jobSeekerId, ct)).Count.ShouldBe(auditBefore);
    }

    [Fact]
    public async Task PUT_top_match_mail_writes_exactly_one_audit_row_carrying_only_the_requested_enabled()
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

    [Fact]
    public async Task PUT_top_match_mail_in_either_direction_leaves_both_consents_and_the_other_switches_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _client.PutAsJsonAsync(FollowPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var consents = (await ReadSeekerAsync(userId, ct)).Preferences;

        foreach (var enabled in new[] { false, true })
        {
            (await _client.PutAsJsonAsync(MailPath, new { enabled }, ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var profile = await GetProfileAsync(ct);
            profile.GetProperty("backgroundMatchNotificationsEnabled").GetBoolean().ShouldBeTrue();
            profile.GetProperty("followedCompanyNotificationsEnabled").GetBoolean().ShouldBeTrue();
            var stored = (await ReadSeekerAsync(userId, ct)).Preferences;
            stored.ShouldBe(consents with { TopMatchMailOffAt = stored.TopMatchMailOffAt });
        }
    }
}
