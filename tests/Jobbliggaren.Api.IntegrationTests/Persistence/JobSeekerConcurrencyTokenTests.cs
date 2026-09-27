using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Persistence;

/// <summary>
/// ADR 0146 — the xmin token on <c>job_seekers</c>, on REAL Postgres, below the replay: a write
/// applied to a row read before a withdrawal committed must fail, never rewrite the preferences
/// document over the withdrawal. One case per writer of the row the token has to stop: the four
/// <c>Preferences</c> writers, and a scan watermark, which writes no preferences at all but is
/// checked all the same because the token covers the whole row.
/// </summary>
[Collection("Api")]
public sealed class JobSeekerConcurrencyTokenTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static readonly Dictionary<string, Action<JobSeeker, IDateTimeProvider>> StaleWrites = new()
    {
        ["SetDigestCadence"] = (seeker, clock) => seeker.SetDigestCadence(DigestCadence.Daily, clock),
        ["ChangeLanguage"] = (seeker, clock) => seeker.ChangeLanguage("en", clock),
        ["UpdateFollowedCompanyNotificationConsent"] =
            (seeker, clock) => seeker.UpdateFollowedCompanyNotificationConsent(true, clock),
        ["UpdateNotificationConsent"] = (seeker, clock) => seeker.UpdateNotificationConsent(true, clock),
        ["AdvanceMatchScan"] = (seeker, clock) => seeker.AdvanceMatchScan(clock.UtcNow, clock),
    };

    public static TheoryData<string> Writers => new(StaleWrites.Keys);

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task A_write_on_a_row_read_before_a_withdrawal_committed_fails_and_the_withdrawal_stands(
        string writer)
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedConsentingSeekerAsync(ct);

        using var staleScope = _factory.Services.CreateScope();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = staleScope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var stale = await staleDb.JobSeekers.SingleAsync(js => js.UserId == userId, ct);

        await WithdrawBackgroundMatchConsentAsync(userId, ct);
        var withdrawn = await ReadSeekerAsync(userId, ct);
        withdrawn.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        withdrawn.Preferences.NotificationConsentWithdrawnAt.ShouldNotBeNull();

        StaleWrites[writer](stale, clock);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync(ct));

        var stored = await ReadSeekerAsync(userId, ct);
        stored.Preferences.ShouldBe(withdrawn.Preferences);
        stored.LastMatchScanAt.ShouldBe(withdrawn.LastMatchScanAt);
        stored.UpdatedAt.ShouldBe(withdrawn.UpdatedAt);
    }

    private async Task<Guid> SeedConsentingSeekerAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        seeker.UpdateNotificationConsent(enabled: true, clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // The actor that commits between the stale read and the stale save: the withdrawal itself,
    // through the aggregate's own method, in a context of its own.
    private async Task WithdrawBackgroundMatchConsentAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var seeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, ct);
        seeker.UpdateNotificationConsent(enabled: false, clock);
        await db.SaveChangesAsync(ct);
    }

    private async Task<JobSeeker> ReadSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, ct);
    }
}
