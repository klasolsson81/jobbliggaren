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
/// ADR 0188 (#2265) — <c>PUT /api/v1/me/junior-quick-link</c>, the switch for the default "Juniora IT-jobb" link in the
/// /jobb hero, end to end on the wired API and real Postgres, in the form of <c>NotificationMailEndpointTests</c>. The
/// link shows by default, each write reads back through <c>GET /api/v1/me/profile</c>, a repeated write is a no-op, and
/// turning the link on over the cap of three quick links is refused. The searches are stored the way production stores
/// them: the capture POST, <c>POST /api/v1/me/recent-searches</c> (#2285).
/// </summary>
[Collection("Api")]
public sealed class JuniorQuickLinkEndpointTests(ApiFactory factory)
{
    private const string JuniorLinkPath = "/api/v1/me/junior-quick-link";
    private const string RecentSearchesPath = "/api/v1/me/recent-searches";
    private const string EventType = "JobSeeker.JuniorQuickLinkUpdated";

    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PUT_junior_quick_link_anonymous_returns_401()
    {
        var response = await _client.PutAsJsonAsync(JuniorLinkPath, new { enabled = false }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_profile_shows_the_junior_link_by_default_and_reads_every_write_back()
    {
        var userId = await AuthenticateAsync();
        (await ProfileAsync()).GetProperty("juniorQuickLinkEnabled").GetBoolean().ShouldBeTrue();

        (await PutAsync(enabled: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ProfileAsync()).GetProperty("juniorQuickLinkEnabled").GetBoolean().ShouldBeFalse();
        var offAt = (await ReadSeekerAsync(userId)).Preferences.JuniorQuickLinkOffAt.ShouldNotBeNull();

        // A repeated "off" is a no-op: the first stamp stays.
        (await PutAsync(enabled: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadSeekerAsync(userId)).Preferences.JuniorQuickLinkOffAt.ShouldBe(offAt);

        (await PutAsync(enabled: true)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ProfileAsync()).GetProperty("juniorQuickLinkEnabled").GetBoolean().ShouldBeTrue();
        (await ReadSeekerAsync(userId)).Preferences.JuniorQuickLinkOffAt.ShouldBeNull();

        (await PutAsync(enabled: true)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ProfileAsync()).GetProperty("juniorQuickLinkEnabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task PUT_junior_quick_link_on_with_three_marked_searches_is_a_409_MaxTotal_and_leaves_it_off()
    {
        var userId = await AuthenticateAsync();
        (await PutAsync(enabled: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        foreach (var q in new[] { "juniorlanka", "juniorlankb", "juniorlankc" })
        {
            var id = await CommitSearchAsync(q);
            (await _client.PutAsync($"{RecentSearchesPath}/{id}/quick-link", content: null, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var response = await PutAsync(enabled: true);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("title").GetString()
            .ShouldBe("QuickLink.MaxTotal");
        (await ProfileAsync()).GetProperty("juniorQuickLinkEnabled").GetBoolean().ShouldBeFalse();
        (await ReadSeekerAsync(userId)).Preferences.JuniorQuickLinkOffAt.ShouldNotBeNull();
    }

    // The command's required member: an empty body must not bind to "off".
    [Fact]
    public async Task PUT_junior_quick_link_without_enabled_returns_400_and_leaves_the_link_shown()
    {
        var userId = await AuthenticateAsync();

        var response = await _client.PutAsJsonAsync(JuniorLinkPath, new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadSeekerAsync(userId)).Preferences.JuniorQuickLinkOffAt.ShouldBeNull();
    }

    // GDPR Art. 5(2), security-auditor's #2265 plan read Q5: the audit row carries the requested enabled and nothing else.
    [Fact]
    public async Task PUT_junior_quick_link_writes_one_audit_row_carrying_only_the_requested_enabled()
    {
        var userId = await AuthenticateAsync();
        var jobSeekerId = (await ReadSeekerAsync(userId)).Id.Value;
        var before = (await ReadAuditEntriesAsync(jobSeekerId)).Select(a => a.Id).ToHashSet();

        (await PutAsync(enabled: false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var entry = (await ReadAuditEntriesAsync(jobSeekerId)).Where(a => !before.Contains(a.Id)).ToList()
            .ShouldHaveSingleItem();
        entry.EventType.ShouldBe(EventType);
        entry.AggregateType.ShouldBe("JobSeeker");
        entry.UserId.ShouldBe(userId);
        using var payload = JsonDocument.Parse(entry.Payload.ShouldNotBeNull());
        var property = payload.RootElement.EnumerateObject().ShouldHaveSingleItem();
        property.Name.ShouldBe("enabled");
        property.Value.ValueKind.ShouldBe(JsonValueKind.False);
    }

    private async Task<Guid> AuthenticateAsync()
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        var me = await _client.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct);
        return Guid.Parse(me.GetProperty("userId").GetString()!);
    }

    private Task<HttpResponseMessage> PutAsync(bool enabled) =>
        _client.PutAsJsonAsync(JuniorLinkPath, new { enabled }, Ct);

    private async Task<JsonElement> ProfileAsync()
    {
        var response = await _client.GetAsync("/api/v1/me/profile", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    // A stored search: the capture POST stores it, as for every search a user stores on /jobb (ADR 0060, #2285).
    private async Task<Guid> CommitSearchAsync(string q)
    {
        (await _client.PostAsJsonAsync(RecentSearchesPath, new { q }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = await _client.GetFromJsonAsync<JsonElement>($"{RecentSearchesPath}?includeCount=false", Ct);
        return items.EnumerateArray()
            .Where(row => row.GetProperty("q").GetString() == q)
            .Select(row => Guid.Parse(row.GetProperty("id").GetString()!))
            .ToList()
            .ShouldHaveSingleItem();
    }

    private async Task<JobSeeker> ReadSeekerAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, Ct);
    }

    private async Task<List<AuditLogEntry>> ReadAuditEntriesAsync(Guid aggregateId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries.AsNoTracking().Where(a => a.AggregateId == aggregateId).ToListAsync(Ct);
    }
}
