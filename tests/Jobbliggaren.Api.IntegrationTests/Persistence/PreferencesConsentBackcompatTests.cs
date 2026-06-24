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
public sealed class PreferencesConsentBackcompatTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly ApiFactory _factory = factory;

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM job_seekers;", TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private async Task<JobSeeker> SeedSeekerAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Consent Backcompat", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private async Task SetRawPreferencesAsync(Guid jobSeekerId, string json, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
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
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.JobSeekers.SingleAsync(js => js.Id == id, ct);
        return reloaded.Preferences;
    }

    [Fact]
    public async Task LegacyRow_WithoutConsentKeys_DeserializesToConsentOff()
    {
        // A `preferences` row written BEFORE ADR 0080: only the three original keys exist,
        // none of the Vag 4 consent keys. The GDPR-critical invariant is that this reads as
        // consent OFF (opt-in default), never an accidental opt-IN and never a crash.
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
        // The pre-existing fields are unchanged (no regression).
        prefs.Language.ShouldBe("sv");
        prefs.EmailNotifications.ShouldBeTrue();
        prefs.WeeklySummary.ShouldBeFalse();
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

    [Fact]
    public async Task EnabledConsent_RoundTripsThroughPreferencesJsonb()
    {
        // The forward path: opt-in stamps consent and round-trips through the owned-JSON.
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Consent On", clock).Value;
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Daily, clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var prefs = await ReloadPreferencesAsync(seeker.Id, ct);

        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.NotificationConsentAt.ShouldNotBeNull();
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }
}
