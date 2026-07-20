using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
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

    // --------------------------- #453 cross-channel dedup - seen-in-app suppression
    // The fixture uses ConsoleEmailSender (no body capture), so the observable proxy for
    // "claimed set == displayed set" is the per-hit notification STATUS: a suppressed hit stays
    // Pending (never claimed -> never in the emailed set) while an unseen hit is drained to Sent. The
    // predicate parity between the claim-fetch and the displayRows-join is what the Application code
    // guarantees; here we prove the end-to-end effect against real Postgres.

    [Fact]
    public async Task RunWeeklyAsync_SuppressesSeenHit_AndDispatchesOnlyUnseen()
    {
        // Two Pending hits for one consenting user; ONE is marked seen-in-app. The seen hit must stay
        // Pending (suppressed, NOT claimed) while the unseen hit is drained to Sent.
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedConsentingWeeklyFollowUserAsync(ct);
        var (seenAdId, seenWatchId) = await SeedAdAndWatchAsync(userId, ct);
        await SeedPendingFollowHitAsync(userId, seenAdId, seenWatchId, ct);
        var (unseenAdId, unseenWatchId) = await SeedAdAndWatchAsync(userId, ct);
        await SeedPendingFollowHitAsync(userId, unseenAdId, unseenWatchId, ct);

        await MarkHitSeenAsync(userId, seenAdId, seenWatchId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct);
        }

        var seenHit = await GetHitAsync(userId, seenAdId, seenWatchId, ct);
        seenHit!.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending,
            "a seen-in-app hit is suppressed - never claimed, never emailed (stays dormant Pending)");
        seenHit.SeenAt.ShouldNotBeNull("the seen stamp survives the run");

        var unseenHit = await GetHitAsync(userId, unseenAdId, unseenWatchId, ct);
        unseenHit!.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent,
            "the unseen hit is the only member of the claimed/displayed set - dispatched to Sent");
    }

    [Fact]
    public async Task RunWeeklyAsync_SendsNoEmail_WhenAllHitsSeen()
    {
        // Every Pending hit for the user is seen-in-app -> the due-set is empty -> no dispatch, no email.
        // Proxy: the hit stays Pending (never claimed).
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedConsentingWeeklyFollowUserAsync(ct);
        var (jobAdId, watchId) = await SeedAdAndWatchAsync(userId, ct);
        await SeedPendingFollowHitAsync(userId, jobAdId, watchId, ct);
        await MarkHitSeenAsync(userId, jobAdId, watchId, ct);

        using (var scope = _fixture.Services.CreateScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>();
            await worker.RunWeeklyAsync(ct);
        }

        var hit = await GetHitAsync(userId, jobAdId, watchId, ct);
        hit!.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending,
            "an all-seen due-set produces no dispatch (the only hit stays dormant Pending)");
    }

    // --------------------------- M-E6 / C-E5 (DPIA Part E §E5/§E7, verified in PR-F3)
    // A user who turns follow-email consent ON only AFTER hits accumulated (7C: hit creation is
    // consent-free, so hits exist while the email flag is OFF) must NOT receive a mass-mail of
    // historical hits they already saw in-app. The SeenAt-suppression (#453) holds ACROSS the opt-in:
    // seen historical hits stay Pending (never emailed); only the UNSEEN hits drain as ONE bounded
    // digest. Observable proxy (ConsoleEmailSender captures no body): per-hit notification STATUS —
    // suppressed=Pending, dispatched=Sent (the file's established proxy, see #453 section above).

    [Fact]
    public async Task RunWeeklyAsync_LateEmailOptIn_DoesNotMassMailSeenHistoricalHits()
    {
        var ct = TestContext.Current.CancellationToken;

        // Email consent initially OFF — but in-app hits still accumulated (7C hit creation is
        // consent-free). Some are opened in-app (SeenAt stamped) BEFORE the opt-in.
        var userId = await SeedWeeklyUserAsync(followConsent: false, ct);

        var seenHistorical = new List<(JobAdId JobAdId, CompanyWatchId WatchId)>();
        for (var i = 0; i < 3; i++)
        {
            var (adId, watchId) = await SeedAdAndWatchAsync(userId, ct);
            await SeedPendingFollowHitAsync(userId, adId, watchId, ct);
            await MarkHitSeenAsync(userId, adId, watchId, ct); // opened in-app while email was OFF
            seenHistorical.Add((adId, watchId));
        }

        var unseen = new List<(JobAdId JobAdId, CompanyWatchId WatchId)>();
        for (var i = 0; i < 2; i++)
        {
            var (adId, watchId) = await SeedAdAndWatchAsync(userId, ct);
            await SeedPendingFollowHitAsync(userId, adId, watchId, ct);
            unseen.Add((adId, watchId));
        }

        // Late opt-in: the email consent flips ON only now, after the hits already exist.
        await SetFollowedCompanyConsentAsync(userId, enabled: true, ct);

        await Should.NotThrowAsync(async () =>
        {
            using var scope = _fixture.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DigestDispatchWorker>().RunWeeklyAsync(ct);
        });

        foreach (var (adId, watchId) in seenHistorical)
        {
            var hit = await GetHitAsync(userId, adId, watchId, ct);
            hit!.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending,
                "en historisk hit som redan setts i appen mass-mejlas ALDRIG vid sen e-post-opt-in " +
                "(SeenAt-suppression håller över opt-in:en) — förblir Pending");
        }

        foreach (var (adId, watchId) in unseen)
        {
            var hit = await GetHitAsync(userId, adId, watchId, ct);
            hit!.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent,
                "endast de osedda hitsen dräneras som EN begränsad digest efter opt-in " +
                "(ingen per-hit-flod av historiska notiser)");
        }
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
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"employer\":{{\"name\":\"Acme AB\",\"organization_number\":\"{orgNr}\"}}}}";
        var jobAd = JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(60),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
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

    // M-E6 — flip the SEPARATE follow-email consent flag (Art. 6(1)(a)) on an existing seeker via the
    // domain method, so a test can model a LATE opt-in (consent turned ON after hits already exist).
    private async Task SetFollowedCompanyConsentAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, ct);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled, new FixedClock(Now));
        await db.SaveChangesAsync(ct);
    }

    // #453 - stamp SeenAt on a Pending hit (the in-app "opened this ad" signal) via the domain method.
    private async Task MarkHitSeenAsync(
        Guid userId, JobAdId jobAdId, CompanyWatchId watchId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hit = await db.FollowedCompanyAdHits.SingleAsync(
            h => h.UserId == userId && h.JobAdId == jobAdId && h.CompanyWatchId == watchId, ct);
        hit.MarkSeen(new FixedClock(Now)).IsSuccess.ShouldBeTrue();
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
