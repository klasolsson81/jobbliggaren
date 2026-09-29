using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
/// ADR 0146 — two writes to one <c>job_seekers</c> row racing through the wired API on REAL
/// Postgres. <see cref="JobSeekerSaveRace"/> holds the first request at SaveChanges, after its
/// handler has read the row, until a second real request from the same session has committed.
/// Before the xmin token the held UPDATE rewrote the whole preferences document it had read, so a
/// withdrawal committed in that window was re-granted; now the held write conflicts and is re-run
/// on a fresh read, and a withdrawal that loses every attempt is refused visibly.
/// </summary>
[Collection("Api")]
public class JobSeekerWriteRaceTests(ApiFactory factory)
{
    private const string CadencePath = "/api/v1/me/digest-cadence";
    private const string ConsentPath = "/api/v1/me/background-match-notification-consent";
    private const string FollowPath = "/api/v1/me/followed-company-notification-consent";
    private const string ProfilePath = "/api/v1/me/profile";
    private const string MatchPreferencesPath = "/api/v1/me/match-preferences";

    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();
    private readonly HttpClient _competitor = factory.CreateClient();

    // Both clients carry the same session: the race is one data subject in two tabs.
    private async Task<Guid> AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, ct: ct);
        var bearer = new AuthenticationHeaderValue("Bearer", sessionId);
        _client.DefaultRequestHeaders.Authorization = bearer;
        _competitor.DefaultRequestHeaders.Authorization = bearer;

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
    public async Task A_withdrawal_committed_inside_a_cadence_save_stands_and_the_cadence_still_lands()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var granted = await ReadSeekerAsync(userId, ct);
        var auditBefore = (await ReadAuditEntriesAsync(granted.Id.Value, ct)).Select(a => a.Id).ToHashSet();

        HttpStatusCode? withdrawalStatus = null;
        Preferences? rightAfterWithdrawal = null;
        _factory.JobSeekerSaveRace.Arm(userId, times: 1, async raceCt =>
        {
            withdrawalStatus = (await _competitor.PutAsJsonAsync(ConsentPath, new { enabled = false }, raceCt))
                .StatusCode;
            rightAfterWithdrawal = (await ReadSeekerAsync(userId, raceCt)).Preferences;
        });
        HttpResponseMessage cadenceSave;
        try
        {
            cadenceSave = await _client.PutAsJsonAsync(CadencePath, new { cadence = "Daily" }, ct);
        }
        finally
        {
            _factory.JobSeekerSaveRace.Disarm();
        }

        _factory.JobSeekerSaveRace.Fired.ShouldBe(1);
        withdrawalStatus.ShouldBe(HttpStatusCode.NoContent);
        cadenceSave.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        rightAfterWithdrawal.ShouldNotBeNull();
        rightAfterWithdrawal.NotificationConsentWithdrawnAt.ShouldNotBeNull();

        var stored = (await ReadSeekerAsync(userId, ct)).Preferences;
        stored.DigestCadence.ShouldBe(DigestCadence.Daily);
        stored.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        stored.NotificationConsentWithdrawnAt.ShouldBe(rightAfterWithdrawal.NotificationConsentWithdrawnAt);
        stored.NotificationConsentAt.ShouldBe(granted.Preferences.NotificationConsentAt);

        var added = (await ReadAuditEntriesAsync(granted.Id.Value, ct))
            .Where(a => !auditBefore.Contains(a.Id))
            .Select(a => a.EventType)
            .ToList();
        added.ShouldBe(
            ["JobSeeker.NotificationConsentUpdated", "JobSeeker.DigestCadenceUpdated"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task A_followed_company_withdrawal_committed_inside_a_language_save_stands_and_the_language_still_lands()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(FollowPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var granted = await ReadSeekerAsync(userId, ct);
        granted.Preferences.Language.ShouldNotBe("en");
        var auditBefore = (await ReadAuditEntriesAsync(granted.Id.Value, ct)).Select(a => a.Id).ToHashSet();

        HttpStatusCode? withdrawalStatus = null;
        Preferences? rightAfterWithdrawal = null;
        _factory.JobSeekerSaveRace.Arm(userId, times: 1, async raceCt =>
        {
            withdrawalStatus = (await _competitor.PutAsJsonAsync(FollowPath, new { enabled = false }, raceCt))
                .StatusCode;
            rightAfterWithdrawal = (await ReadSeekerAsync(userId, raceCt)).Preferences;
        });
        HttpResponseMessage languageSave;
        try
        {
            languageSave = await _client.PatchAsJsonAsync(ProfilePath, new { language = "en" }, ct);
        }
        finally
        {
            _factory.JobSeekerSaveRace.Disarm();
        }

        _factory.JobSeekerSaveRace.Fired.ShouldBe(1);
        withdrawalStatus.ShouldBe(HttpStatusCode.NoContent);
        languageSave.StatusCode.ShouldBe(HttpStatusCode.OK);
        rightAfterWithdrawal.ShouldNotBeNull();
        rightAfterWithdrawal.FollowedCompanyNotificationConsentWithdrawnAt.ShouldNotBeNull();

        var stored = (await ReadSeekerAsync(userId, ct)).Preferences;
        stored.Language.ShouldBe("en");
        stored.FollowedCompanyNotificationsEnabled.ShouldBeFalse();
        stored.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBe(
            rightAfterWithdrawal.FollowedCompanyNotificationConsentWithdrawnAt);
        stored.FollowedCompanyNotificationConsentAt.ShouldBe(
            granted.Preferences.FollowedCompanyNotificationConsentAt);

        var added = (await ReadAuditEntriesAsync(granted.Id.Value, ct))
            .Where(a => !auditBefore.Contains(a.Id))
            .Select(a => a.EventType)
            .ToList();
        added.ShouldBe(
            ["JobSeeker.FollowedCompanyNotificationConsentUpdated", "JobSeeker.ProfileUpdated"],
            ignoreOrder: true);
    }

    // The cap: a withdrawal whose row is written again before every one of its attempts commits is
    // refused with 409, and nothing of it is stored — the consent is still on, and no consent row
    // records a withdrawal that did not happen.
    [Fact]
    public async Task A_withdrawal_that_loses_every_attempt_is_refused_with_409_and_writes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await _client.PutAsJsonAsync(ConsentPath, new { enabled = true }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var granted = await ReadSeekerAsync(userId, ct);
        var auditBefore = (await ReadAuditEntriesAsync(granted.Id.Value, ct)).Select(a => a.Id).ToHashSet();

        var competingStatuses = new List<HttpStatusCode>();
        string[] cadences = ["Daily", "Weekly", "Daily"];
        _factory.JobSeekerSaveRace.Arm(userId, times: 3, async raceCt =>
        {
            var cadence = cadences[competingStatuses.Count];
            competingStatuses.Add(
                (await _competitor.PutAsJsonAsync(CadencePath, new { cadence }, raceCt)).StatusCode);
        });
        HttpResponseMessage withdrawal;
        try
        {
            withdrawal = await _client.PutAsJsonAsync(ConsentPath, new { enabled = false }, ct);
        }
        finally
        {
            _factory.JobSeekerSaveRace.Disarm();
        }

        _factory.JobSeekerSaveRace.Fired.ShouldBe(3);
        competingStatuses.ShouldBe([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent]);
        withdrawal.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var stored = (await ReadSeekerAsync(userId, ct)).Preferences;
        stored.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        stored.NotificationConsentWithdrawnAt.ShouldBeNull();
        stored.NotificationConsentAt.ShouldBe(granted.Preferences.NotificationConsentAt);
        stored.DigestCadence.ShouldBe(DigestCadence.Daily);

        var added = (await ReadAuditEntriesAsync(granted.Id.Value, ct))
            .Where(a => !auditBefore.Contains(a.Id))
            .Select(a => a.EventType)
            .ToList();
        added.ShouldBe(
            ["JobSeeker.DigestCadenceUpdated", "JobSeeker.DigestCadenceUpdated", "JobSeeker.DigestCadenceUpdated"]);
    }

    private static Task<HttpResponseMessage> PatchMatchPreferencesAsync(
        HttpClient client, string json, CancellationToken ct) =>
        client.PatchAsync(MatchPreferencesPath, new StringContent(json, Encoding.UTF8, "application/json"), ct);

    // #1918 — two tabs writing different parts of the match preferences. The held skills
    // request has read the row before the other tab's occupations commit; its replay reads that commit
    // and lays only the skills over it.
    [Fact]
    public async Task An_occupations_patch_committed_inside_a_skills_patch_stands_and_the_skills_still_land()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await AuthenticateAsync(ct);
        (await PatchMatchPreferencesAsync(
            _client,
            """
            {
              "occupations": {"preferredOccupationGroups": ["grp_a", "grp_b"], "preferredOccupationExperience": [{"conceptId": "grp_a", "years": 4}]},
              "skills": {"preferredSkills": ["sk_a", "sk_b"]},
              "locations": {"preferredRegions": ["reg_a"], "preferredMunicipalities": ["kn_a"], "preferredRemote": true},
              "employmentTypes": {"preferredEmploymentTypes": ["et_a"]},
              "experience": {"experienceYears": 7}
            }
            """,
            ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var seeded = (await ReadSeekerAsync(userId, ct)).MatchPreferences;
        seeded.ShouldBe(MatchPreferences.Create(
            preferredOccupationGroups: ["grp_a", "grp_b"],
            preferredRegions: ["reg_a"],
            preferredEmploymentTypes: ["et_a"],
            preferredMunicipalities: ["kn_a"],
            preferredSkills: ["sk_a", "sk_b"],
            experienceYears: 7,
            preferredOccupationExperience: [new OccupationExperience("grp_a", 4)],
            preferredRemote: true).Value);

        HttpStatusCode? occupationsStatus = null;
        MatchPreferences? rightAfterOccupations = null;
        _factory.JobSeekerSaveRace.Arm(userId, times: 1, async raceCt =>
        {
            occupationsStatus = (await PatchMatchPreferencesAsync(
                _competitor,
                """{"occupations":{"preferredOccupationGroups":["grp_c"],"preferredOccupationExperience":[{"conceptId":"grp_c","years":2}]}}""",
                raceCt)).StatusCode;
            rightAfterOccupations = (await ReadSeekerAsync(userId, raceCt)).MatchPreferences;
        });
        HttpResponseMessage skillsSave;
        try
        {
            skillsSave = await PatchMatchPreferencesAsync(
                _client, """{"skills":{"preferredSkills":["sk_z"]}}""", ct);
        }
        finally
        {
            _factory.JobSeekerSaveRace.Disarm();
        }

        _factory.JobSeekerSaveRace.Fired.ShouldBe(1);
        occupationsStatus.ShouldBe(HttpStatusCode.NoContent);
        skillsSave.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        rightAfterOccupations.ShouldNotBeNull();
        rightAfterOccupations.PreferredOccupationGroups.ShouldBe(["grp_c"]);
        rightAfterOccupations.PreferredSkills.ShouldBe(seeded.PreferredSkills);

        var stored = (await ReadSeekerAsync(userId, ct)).MatchPreferences;
        stored.PreferredOccupationGroups.ShouldBe(["grp_c"]);
        stored.PreferredOccupationExperience.ShouldBe([new OccupationExperience("grp_c", 2)]);
        stored.PreferredSkills.ShouldBe(["sk_z"]);
        stored.PreferredRegions.ShouldBe(seeded.PreferredRegions);
        stored.PreferredMunicipalities.ShouldBe(seeded.PreferredMunicipalities);
        stored.PreferredRemote.ShouldBe(seeded.PreferredRemote);
        stored.PreferredEmploymentTypes.ShouldBe(seeded.PreferredEmploymentTypes);
        stored.ExperienceYears.ShouldBe(seeded.ExperienceYears);
    }
}
