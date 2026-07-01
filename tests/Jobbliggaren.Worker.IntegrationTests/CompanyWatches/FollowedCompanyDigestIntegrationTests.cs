using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.Hosting;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — Testcontainers integration test for the company-follow digest pass of
/// <see cref="Jobbliggaren.Application.Matching.Jobs.DigestDispatch.DigestDispatchJob"/> +
/// <see cref="DigestDispatchWorker"/> against REAL Postgres + the fixture's ConsoleEmailSender
/// (test env → no real send). Proves the SAME Worker digest entry point ALSO drains company-follow
/// hits (the independent second pass, ADR 0087 D5) — the consent predicate (the SEPARATE
/// FollowedCompanyNotificationsEnabled flag on the shared cadence), the Pending filter, and the
/// join to public job_ads all run against real jsonb/columns hidden by EF-InMemory. Mirrors
/// <see cref="Jobbliggaren.Worker.IntegrationTests.Matching.DigestDispatchJobIntegrationTests"/>.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class FollowedCompanyDigestIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunWeeklyAsync_DrainsFollowHit_ForConsentingWeeklyUser()
    {
        var ct = TestContext.Current.CancellationToken;

        var userId = await SeedConsentingWeeklyFollowUserAsync(ct);
        var (jobAdId, watchId) = await SeedAdAndWatchAsync(userId, ct);
        await SeedPendingFollowHitAsync(userId, jobAdId, watchId, ct);

        await Should.NotThrowAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct);
        });

        var hit = await GetHitAsync(userId, jobAdId, watchId, ct);
        hit.ShouldNotBeNull();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent,
            "a consenting Weekly follow user's pending hit should be digested and marked Sent");
    }

    [Fact]
    public async Task RunWeeklyAsync_LeavesFollowHitPending_ForNonConsentingUser()
    {
        var ct = TestContext.Current.CancellationToken;

        // Follow-consent OFF (default) — but seed a hit anyway (e.g. consent withdrawn after the scan).
        var userId = await SeedWeeklyUserAsync(followConsent: false, ct);
        var (jobAdId, watchId) = await SeedAdAndWatchAsync(userId, ct);
        await SeedPendingFollowHitAsync(userId, jobAdId, watchId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct);
        }

        var hit = await GetHitAsync(userId, jobAdId, watchId, ct);
        hit.ShouldNotBeNull();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending,
            "a non-consenting user's follow hit must not be dispatched (consent is the query gate)");
    }

    [Fact]
    public async Task RunWeeklyAsync_LeavesFollowHitPending_ForDailyCadenceUser()
    {
        // Cadence mismatch: a follow-consenting DAILY user is NOT dispatched on a WEEKLY run (the cron
        // IS the window). Guards against a cadence bug double- or mis-dispatching.
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedFollowUserAsync(followConsent: true, DigestCadence.Daily, ct);
        var (jobAdId, watchId) = await SeedAdAndWatchAsync(userId, ct);
        await SeedPendingFollowHitAsync(userId, jobAdId, watchId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct); // weekly run — the daily user is NOT due
        }

        var hit = await GetHitAsync(userId, jobAdId, watchId, ct);
        hit.ShouldNotBeNull();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending,
            "a Daily-cadence user's hit must not be dispatched on a Weekly run");
    }

    [Fact]
    public async Task RunWeeklyAsync_DrainsWithoutEmail_WhenFollowedAdRetracted()
    {
        // Every followed ad retracted/purged since the hit was created → the display join is empty →
        // the claimed rows are drained (Pending→Sent) WITHOUT an email, so the empty window is not
        // re-processed every run.
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedConsentingWeeklyFollowUserAsync(ct);
        var (phantomAdId, watchId) = await SeedHitForMissingAdAsync(userId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct);
        }

        var hit = await GetHitAsync(userId, phantomAdId, watchId, ct);
        hit.ShouldNotBeNull();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent,
            "an all-retracted window is drained (marked Sent) so it does not re-process every run");
    }

    [Fact]
    public async Task RunWeeklyAsync_LeavesFollowHitQueued_WhenUserHasNoAccountEmail()
    {
        // A consenting user with a JobSeeker but NO Identity account email → the hit is claimed
        // (Pending→Queued) but left Queued (never Sent) — the claim-then-send spine's "never
        // double-email" posture (TD-114). Proves a send that never happens does not re-notify.
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedFollowUserAsync(
            followConsent: true, DigestCadence.Weekly, ct, withIdentityEmail: false);
        var (jobAdId, watchId) = await SeedAdAndWatchAsync(userId, ct);
        await SeedPendingFollowHitAsync(userId, jobAdId, watchId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct);
        }

        var hit = await GetHitAsync(userId, jobAdId, watchId, ct);
        hit.ShouldNotBeNull();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Queued,
            "a hit for a user with no account email is claimed then left Queued (never Sent)");
    }

    [Fact]
    public async Task RunWeeklyAsync_DrainsAllRows_BeyondTheDisplayCap()
    {
        // Anti-spam cap: with more than MaxItemsPerDigest (default 20) pending hits, the email lists
        // at most the cap but ALL window rows are drained (marked Sent), so the un-displayed remainder
        // cannot re-surface next digest.
        var ct = TestContext.Current.CancellationToken;
        const int hitCount = 22; // > default cap of 20
        var userId = await SeedConsentingWeeklyFollowUserAsync(ct);
        var seeded = new List<(JobAdId JobAdId, CompanyWatchId WatchId)>();
        for (var i = 0; i < hitCount; i++)
        {
            var (jobAdId, watchId) = await SeedAdAndWatchAsync(userId, ct);
            await SeedPendingFollowHitAsync(userId, jobAdId, watchId, ct);
            seeded.Add((jobAdId, watchId));
        }

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct);
        }

        foreach (var (jobAdId, watchId) in seeded)
        {
            var hit = await GetHitAsync(userId, jobAdId, watchId, ct);
            hit!.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent,
                "ALL window rows drain (not just the displayed cap) so the remainder cannot re-surface");
        }
    }

    [Fact]
    public async Task RunWeeklyAsync_Twice_DoesNotReDispatch()
    {
        // Idempotent claim-then-send: the second run finds the hit Sent (not Pending), never re-picks
        // it, sends no second email. SentAt is unchanged.
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedConsentingWeeklyFollowUserAsync(ct);
        var (jobAdId, watchId) = await SeedAdAndWatchAsync(userId, ct);
        await SeedPendingFollowHitAsync(userId, jobAdId, watchId, ct);

        using (var scope = _fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>().RunWeeklyAsync(ct);
        var afterFirst = await GetHitAsync(userId, jobAdId, watchId, ct);
        afterFirst!.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
        var firstSentAt = afterFirst.SentAt;

        await Should.NotThrowAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>().RunWeeklyAsync(ct);
        });

        var afterSecond = await GetHitAsync(userId, jobAdId, watchId, ct);
        afterSecond!.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
        afterSecond.SentAt.ShouldBe(firstSentAt, "a re-run must not re-dispatch (SentAt unchanged)");
    }

    // ─────────────────────────── Seeding

    private Task<Guid> SeedConsentingWeeklyFollowUserAsync(CancellationToken ct)
        => SeedFollowUserAsync(followConsent: true, DigestCadence.Weekly, ct);

    private Task<Guid> SeedWeeklyUserAsync(bool followConsent, CancellationToken ct)
        => SeedFollowUserAsync(followConsent, DigestCadence.Weekly, ct);

    private async Task<Guid> SeedFollowUserAsync(
        bool followConsent, DigestCadence cadence, CancellationToken ct, bool withIdentityEmail = true)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        Guid userId;
        if (withIdentityEmail)
        {
            var email = $"follow-{Guid.NewGuid():N}@test.local";
            var user = new ApplicationUser { UserName = email, Email = email };
            (await userManager.CreateAsync(user, "FollowPass123!")).Succeeded.ShouldBeTrue();
            userId = user.Id;
        }
        else
        {
            // No Identity user → GetEmailAsync returns null (the orphan-consent no-email path).
            userId = Guid.NewGuid();
        }

        var jobSeeker = JobSeeker.Register(userId, "Follow Seed", clock).Value;
        // The digest cadence is the SHARED DigestCadence (ADR 0087 D2) — set it via the background-
        // match consent path (cadence is one preference), then gate the FOLLOW pass on the separate
        // follow flag.
        jobSeeker.UpdateNotificationConsent(enabled: false, cadence, clock);
        if (followConsent)
            jobSeeker.UpdateFollowedCompanyNotificationConsent(enabled: true, clock);

        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // Seeds a Pending hit whose watch is real but whose ad row does NOT exist (a purged/retracted ad;
    // JobAdId is a by-identity no-FK soft-reference, so this is legitimate) — the display join finds
    // no ad, driving the empty-drain path.
    private async Task<(JobAdId JobAdId, CompanyWatchId WatchId)> SeedHitForMissingAdAsync(
        Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000).ToString(
            "D8", System.Globalization.CultureInfo.InvariantCulture);
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, clock).Value;
        db.CompanyWatches.Add(watch);
        var phantomAdId = JobAdId.New(); // no job_ads row
        db.FollowedCompanyAdHits.Add(
            FollowedCompanyAdHit.Create(userId, phantomAdId, watch.Id, clock).Value);
        await db.SaveChangesAsync(ct);
        return (phantomAdId, watch.Id);
    }

    private async Task<(JobAdId JobAdId, CompanyWatchId WatchId)> SeedAdAndWatchAsync(
        Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000).ToString(
            "D8", System.Globalization.CultureInfo.InvariantCulture);

        var externalId = $"fd-{Guid.NewGuid():N}";
        var jobAd = JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\",\"employer\":{{\"name\":\"Acme AB\",\"organization_number\":\"{orgNr}\"}}}}",
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(60),
            clock: clock).Value;
        db.JobAds.Add(jobAd);

        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, clock).Value;
        db.CompanyWatches.Add(watch);

        await db.SaveChangesAsync(ct);
        return (jobAd.Id, watch.Id);
    }

    private async Task SeedPendingFollowHitAsync(
        Guid userId, JobAdId jobAdId, CompanyWatchId watchId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hit = FollowedCompanyAdHit.Create(userId, jobAdId, watchId, new FixedClock(Now)).Value;
        db.FollowedCompanyAdHits.Add(hit);
        await db.SaveChangesAsync(ct);
    }

    private async Task<FollowedCompanyAdHit?> GetHitAsync(
        Guid userId, JobAdId jobAdId, CompanyWatchId watchId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FollowedCompanyAdHits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                h => h.UserId == userId && h.JobAdId == jobAdId && h.CompanyWatchId == watchId, ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
