using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Persistence;

// ADR 0080 Vag 4 PR-1 — the GDPR-critical back-compat proof for the new consent fields on
// the `Preferences` value object (job_seekers.preferences jsonb, mapped via EF
// OwnsOne(...).ToJson()). dotnet-architect Major: the "additive jsonb, missing keys ->
// safe defaults (consent OFF)" claim must be PROVEN, not asserted — and EF owned-JSON does
// NOT share the tolerant custom-converter contract that MatchPreferences uses. consent OFF
// is "non-negotiable" (ADR 0080 Beslut 5), so a legacy `preferences` row written BEFORE
// ADR 0080 (lacking the new keys) MUST deserialize to BackgroundMatchNotificationsEnabled
// == false (and consent timestamps null), never a crash and never an accidental opt-IN.
[Collection("Api")]
public sealed class PreferencesConsentBackcompatTests(ApiFactory factory)
    : MalformedJsonbSeedTestBase(factory)
{
    protected override IReadOnlyList<string> TablesToClear => ["job_seekers"];

    private async Task<JobSeeker> SeedSeekerAsync(CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(clock), clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private async Task SetRawPreferencesAsync(Guid jobSeekerId, string json, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE job_seekers SET preferences = @json::jsonb WHERE id = @id";
        cmd.Parameters.AddWithValue("json", json);
        cmd.Parameters.AddWithValue("id", jobSeekerId);
        await cmd.ExecuteNonQueryAsync(ct);
        await conn.CloseAsync();
    }

    private async Task<Preferences> ReloadPreferencesAsync(JobSeekerId id, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.JobSeekers.SingleAsync(js => js.Id == id, ct);
        return reloaded.Preferences;
    }

    private async Task<List<string>> StoredKeysAsync(Guid jobSeekerId, CancellationToken ct)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Database.SqlQuery<string>(
                $"""SELECT jsonb_object_keys(preferences) AS "Value" FROM job_seekers WHERE id = {jobSeekerId}""")
            .ToListAsync(ct);
    }

    [Fact]
    public async Task LegacyRow_WithoutConsentKeys_DeserializesToConsentOff()
    {
        // A `preferences` row written BEFORE ADR 0080: only the three original keys exist,
        // none of the Vag 4 consent keys. The GDPR-critical invariant is that this reads as
        // consent OFF (opt-in default), never an accidental opt-IN and never a crash.
        //
        // TD-115: the EmailNotifications/WeeklySummary keys are now UNMAPPED (the flags were
        // retired) — EF OwnsOne(...).ToJson() ignores them on read, so this legacy row still
        // deserializes cleanly (the unmapped-key tolerance the no-migration retire relies on).
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(ct);

        var legacyJson = """
            {"Language":"sv","EmailNotifications":true,"WeeklySummary":false}
            """;
        await SetRawPreferencesAsync(seeker.Id.Value, legacyJson, ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBeNull();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
        // The surviving field is unchanged (no regression).
        prefs.Language.ShouldBe("sv");
    }

    [Fact]
    public async Task PreTd115Row_WithRetiredFlagKeysAndConsent_DeserializesCleanly()
    {
        // TD-115 retire back-compat proof (the CTO-mandated, GDPR-grade evidence): a row
        // written BETWEEN ADR 0080 and TD-115 carries BOTH the now-retired
        // EmailNotifications/WeeklySummary keys AND a live Vag 4 consent. After the retire,
        // the unmapped legacy keys must be silently ignored (no crash) while every surviving
        // field — crucially the opt-IN consent + its Art. 7 timestamp — round-trips intact.
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(ct);

        var preTd115Json = """
            {"Language":"en","EmailNotifications":true,"WeeklySummary":true,"BackgroundMatchNotificationsEnabled":true,"DigestCadence":0,"NotificationConsentAt":"2026-06-24T03:20:00+00:00","NotificationConsentWithdrawnAt":null}
            """;
        await SetRawPreferencesAsync(seeker.Id.Value, preTd115Json, ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.Language.ShouldBe("en");
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.NotificationConsentAt.ShouldNotBeNull();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public async Task BareEmptyObject_DeserializesToAllSafeDefaults_ConsentOff()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(ct);
        await SetRawPreferencesAsync(seeker.Id.Value, "{}", ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBeNull();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    // ADR 0164 (#2107) — the actor is every row written before migration AddNotificationMailPauses: its
    // writer's Preferences had no pause keys, which the current writer emits
    // (CurrentWriter_EmitsEveryPreferencesParameterAsAKey). All three stamps must read as null, which is
    // "on", and the consents beside them must survive.
    [Fact]
    public async Task LegacyRow_WrittenBeforeAddNotificationMailPauses_ReadsEveryMailSwitchOn()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(ct);
        string[] pauseKeys =
        [
            nameof(Preferences.NotificationMailOffAt),
            nameof(Preferences.TopMatchMailOffAt),
            nameof(Preferences.MatchSummaryMailOffAt),
        ];
        pauseKeys.ShouldBeSubsetOf(await StoredKeysAsync(seeker.Id.Value, ct));

        var beforePausesJson = """
            {"Language":"sv","BackgroundMatchNotificationsEnabled":true,"DigestCadence":1,"NotificationConsentAt":"2026-09-01T08:00:00+00:00","NotificationConsentWithdrawnAt":null,"FollowedCompanyNotificationsEnabled":true,"FollowedCompanyNotificationConsentAt":"2026-09-02T08:00:00+00:00","FollowedCompanyNotificationConsentWithdrawnAt":null}
            """;
        await SetRawPreferencesAsync(seeker.Id.Value, beforePausesJson, ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.NotificationMailOffAt.ShouldBeNull();
        prefs.TopMatchMailOffAt.ShouldBeNull();
        prefs.MatchSummaryMailOffAt.ShouldBeNull();
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        prefs.DigestCadence.ShouldBe(DigestCadence.Weekly);
    }

    // architect Nice-to-have 2 (#2107): a missing key reads as its type's CLR default, never as the
    // record's parameter default. The actor is a row written before ADR 0080 added DigestCadence, which
    // the current writer emits (CurrentWriter_EmitsEveryPreferencesParameterAsAKey), the shape of
    // LegacyRow_WithoutConsentKeys_DeserializesToConsentOff above.
    [Fact]
    public async Task LegacyRow_WithoutDigestCadenceKey_ReadsDaily_TheClrDefault_NotTheRecordDefaultWeekly()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(ct);
        (await StoredKeysAsync(seeker.Id.Value, ct)).ShouldContain(nameof(Preferences.DigestCadence));
        new Preferences().DigestCadence.ShouldBe(DigestCadence.Weekly);
        default(DigestCadence).ShouldBe(DigestCadence.Daily);

        await SetRawPreferencesAsync(
            seeker.Id.Value, """{"Language":"sv","EmailNotifications":true,"WeeklySummary":false}""", ct);

        (await ReloadPreferencesAsync(seeker.Id, ct)).DigestCadence.ShouldBe(DigestCadence.Daily);
    }

    [Fact]
    public async Task EnabledConsent_RoundTripsThroughPreferencesJsonb()
    {
        // The forward path: opt-in stamps consent and round-trips through the owned-JSON.
        var ct = TestContext.Current.CancellationToken;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(clock), clock).Value;
        seeker.UpdateNotificationConsent(enabled: true, clock);
        seeker.SetDigestCadence(DigestCadence.Daily, clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.NotificationConsentAt.ShouldNotBeNull();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public async Task CurrentWriter_EmitsEveryPreferencesParameterAsAKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var seeker = await SeedSeekerAsync(ct);
        var parameterNames = typeof(Preferences).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.Name!)
            .ToList();

        parameterNames.ShouldBeSubsetOf(await StoredKeysAsync(seeker.Id.Value, ct));
    }
}
