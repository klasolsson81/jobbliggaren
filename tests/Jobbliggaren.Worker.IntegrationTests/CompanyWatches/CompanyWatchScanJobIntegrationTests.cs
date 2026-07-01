using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — Testcontainers integration tests for <see cref="CompanyWatchScanJob"/>
/// against REAL Postgres. NEVER EF-InMemory: the org.nr membership matches the STORED generated
/// <c>organization_number</c> column (<c>raw_payload-&gt;'employer'-&gt;&gt;'organization_number'</c>) via
/// <c>EF.Property + IN</c> — only Postgres computes that column (hidden by InMemory). The consent
/// predicate also runs against the real jsonb <c>preferences</c> column.
/// <para>
/// The job is CONSTRUCTED DIRECTLY (<c>new CompanyWatchScanJob(...)</c>, parity
/// <c>BackgroundMatchingJobIntegrationTests</c>). An injected <see cref="FixedClock"/> makes the
/// cold-start floor + watermark deterministic.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchScanJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset Now = new(2026, 6, 15, 3, 25, 0, TimeSpan.Zero);

    // Per-test-unique org.nrs (the [Collection] shares one Postgres; the scan has no filter knob and
    // matches EVERY active ad whose org.nr is watched, so tests must not cross-contaminate).
    private static string UniqueLegalOrgNr() =>
        // 10 digits, third digit ≥ 2 → a legal-entity org.nr (NOT personnummer-shaped). Random-ish
        // but deterministic per call via a fresh Guid hash mapped into the 55xxxxxxxx space.
        "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000).ToString(
            "D8", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task RunAsync_PersistsFollowHit_AndAdvancesWatermark_WhenConsentingUserFollowsMatchingAd()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        var watchId = await SeedWatchAsync(userId, orgNr, ct);
        var jobAdId = await SeedAdWithOrgNrAsync(orgNr, "Acme Bygg AB", ct);

        await RunJobAsync(ct);

        var hits = await GetHitsAsync(userId, ct);
        var hit = hits.ShouldHaveSingleItem();
        hit.JobAdId.ShouldBe(jobAdId);
        hit.CompanyWatchId.ShouldBe(watchId);
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);

        var seeker = await GetSeekerAsync(userId, ct);
        seeker.LastCompanyWatchScanAt.ShouldBe(Now, "the watermark advances to the scan instant");
    }

    [Fact]
    public async Task RunAsync_DoesNotPersistHit_ForUnwatchedOrgNr()
    {
        var ct = TestContext.Current.CancellationToken;
        var watchedOrgNr = UniqueLegalOrgNr();
        var otherOrgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchAsync(userId, watchedOrgNr, ct);
        // An ad from an employer the user does NOT follow.
        await SeedAdWithOrgNrAsync(otherOrgNr, "Other AB", ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(userId, ct)).ShouldBeEmpty(
            "an ad whose org.nr is not watched must not produce a follow hit");
    }

    [Fact]
    public async Task RunAsync_OnlyConsentingUsersAreScanned_OffAndWithdrawnExcluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();

        var (offUserId, _) = await SeedUserAsync(FollowConsent.Off, ct);
        await SeedWatchAsync(offUserId, orgNr, ct);
        var (withdrawnUserId, _) = await SeedUserAsync(FollowConsent.Withdrawn, ct);
        await SeedWatchAsync(withdrawnUserId, orgNr, ct);
        var (onUserId, _) = await SeedUserAsync(FollowConsent.On, ct);
        await SeedWatchAsync(onUserId, orgNr, ct);

        await SeedAdWithOrgNrAsync(orgNr, "Shared AB", ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(offUserId, ct)).ShouldBeEmpty("consent OFF → not scanned");
        (await GetHitsAsync(withdrawnUserId, ct)).ShouldBeEmpty("consent withdrawn → not scanned");
        (await GetHitsAsync(onUserId, ct)).Count.ShouldBe(1, "consent ON → one follow hit");
    }

    [Fact]
    public async Task RunAsync_RunTwice_DoesNotDuplicateHits_NoDbUpdateException()
    {
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchAsync(userId, orgNr, ct);
        await SeedAdWithOrgNrAsync(orgNr, "Acme AB", ct);

        await RunJobAsync(ct);
        // Second run against the SAME watermark-advanced state — the dedup UNIQUE + watermark make it
        // a clean no-op (never throws, never a second row).
        await Should.NotThrowAsync(async () => await RunJobAsync(ct));

        (await GetHitsAsync(userId, ct)).Count.ShouldBe(1, "the dedup spine prevents a duplicate hit");
    }

    [Fact]
    public async Task RunAsync_AdvancesWatermark_ForConsentingUserWithNoWatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var (userId, _) = await SeedConsentingUserAsync(ct);
        // No CompanyWatch seeded.

        await RunJobAsync(ct);

        var seeker = await GetSeekerAsync(userId, ct);
        seeker.LastCompanyWatchScanAt.ShouldBe(Now,
            "a consenting user with no follows still advances the watermark (a later follow only " +
            "catches future ads)");
        (await GetHitsAsync(userId, ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_ExcludesSoftDeletedUnfollowedWatch()
    {
        // A watch the user UNFOLLOWED (soft-deleted) must NOT produce a hit even if a new ad from that
        // org.nr appears — the scan relies on the CompanyWatch soft-delete query filter. Regression
        // trap: a future IgnoreQueryFilters() slip would silently re-notify from an unfollowed employer.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedUnfollowedWatchAsync(userId, orgNr, ct);
        await SeedAdWithOrgNrAsync(orgNr, "Ex-Followed AB", ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(userId, ct)).ShouldBeEmpty(
            "an unfollowed (soft-deleted) watch must not produce a follow hit");
    }

    [Fact]
    public async Task RunAsync_ScansPersonnummerShapedOrgNr_LikeAnyOtherWatch()
    {
        // D8: the personnummer guard lives at the SURFACING/LOG boundary, NOT at scan membership. A
        // followed enskild-firma org.nr (personnummer-shaped, third digit < 2) must scan and hit
        // normally — the guard must not accidentally block a legitimate follow.
        var ct = TestContext.Current.CancellationToken;
        var pnrShapedOrgNr = UniquePersonnummerShapedOrgNr();
        OrganizationNumber.FromTrusted(pnrShapedOrgNr).IsPersonnummerShaped().ShouldBeTrue(
            "test fixture must actually be personnummer-shaped");
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchAsync(userId, pnrShapedOrgNr, ct);
        await SeedAdWithOrgNrAsync(pnrShapedOrgNr, "Enskild Firma Karlsson", ct);

        await RunJobAsync(ct);

        (await GetHitsAsync(userId, ct)).Count.ShouldBe(1,
            "a personnummer-shaped (enskild firma) org.nr is scanned like any other watch");
    }

    [Fact]
    public async Task RunAsync_ColdStart_ExcludesAdOlderThanFloor_IncludesAdWithinFloor()
    {
        // A never-scanned user (null watermark) seeds a 7-day ColdStartDays floor: an ad ingested
        // before the floor is NOT a hit; one within it IS. Pins the since = now.AddDays(-ColdStartDays)
        // branch at the boundary.
        var ct = TestContext.Current.CancellationToken;
        var orgNr = UniqueLegalOrgNr();
        var (userId, _) = await SeedConsentingUserAsync(ct);
        await SeedWatchAsync(userId, orgNr, ct);
        var oldAdId = await SeedAdWithOrgNrAtAsync(orgNr, "Old AB", Now.AddDays(-8), ct);
        var freshAdId = await SeedAdWithOrgNrAtAsync(orgNr, "Fresh AB", Now.AddDays(-3), ct);

        await RunJobAsync(ct);

        var hits = await GetHitsAsync(userId, ct);
        hits.Select(h => h.JobAdId).ShouldContain(freshAdId, "an ad within the 7-day cold-start floor hits");
        hits.Select(h => h.JobAdId).ShouldNotContain(oldAdId, "an ad older than the cold-start floor is excluded");
    }

    // ─────────────────────────── Seeding helpers

    private enum FollowConsent { Off, On, Withdrawn }

    private Task<(Guid UserId, JobSeekerId JobSeekerId)> SeedConsentingUserAsync(CancellationToken ct)
        => SeedUserAsync(FollowConsent.On, ct);

    private async Task<(Guid UserId, JobSeekerId JobSeekerId)> SeedUserAsync(
        FollowConsent consent, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var jobSeeker = JobSeeker.Register(Guid.NewGuid(), "Follow Seed", clock).Value;
        switch (consent)
        {
            case FollowConsent.On:
                jobSeeker.UpdateFollowedCompanyNotificationConsent(true, clock);
                break;
            case FollowConsent.Withdrawn:
                jobSeeker.UpdateFollowedCompanyNotificationConsent(true, clock);
                jobSeeker.UpdateFollowedCompanyNotificationConsent(false, clock);
                break;
            case FollowConsent.Off:
            default:
                break; // default OFF
        }

        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(ct);
        return (jobSeeker.UserId, jobSeeker.Id);
    }

    private async Task<CompanyWatchId> SeedWatchAsync(Guid userId, string orgNr, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var watch = CompanyWatch.Follow(
            userId, OrganizationNumber.Create(orgNr).Value, clock).Value;
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    // Seeds a watch then immediately UNFOLLOWS it (soft-delete) — the query filter should hide it.
    private async Task SeedUnfollowedWatchAsync(Guid userId, string orgNr, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);

        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, clock).Value;
        watch.SoftDelete(clock);
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
    }

    // A personnummer-shaped 10-digit org.nr (third digit < 2 → enskild firma / potential personnummer).
    private static string UniquePersonnummerShapedOrgNr() =>
        "90" + "0" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 10000000).ToString(
            "D7", System.Globalization.CultureInfo.InvariantCulture);

    // Imports a public job_ad whose raw_payload carries employer.organization_number, so the STORED
    // generated `organization_number` column auto-populates (mirrors CompanyWatchesTests /
    // ListJobAdsEmployerFilterTests). CreatedAt = Now (deterministic ingest time).
    private Task<JobAdId> SeedAdWithOrgNrAsync(string orgNr, string companyName, CancellationToken ct)
        => SeedAdWithOrgNrAtAsync(orgNr, companyName, Now, ct);

    // As above, but the ad's CreatedAt (ingest time) is set to `createdAt` (for the cold-start window).
    private async Task<JobAdId> SeedAdWithOrgNrAtAsync(
        string orgNr, string companyName, DateTimeOffset createdAt, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = $"cw-scan-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\"," +
            $"\"employer\":{{\"name\":\"{companyName}\",\"organization_number\":\"{orgNr}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Snickare",
            company: Company.Create(companyName).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: createdAt.AddDays(-1),
            expiresAt: createdAt.AddDays(60),
            clock: new FixedClock(createdAt)).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private async Task RunJobAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var job = new CompanyWatchScanJob(
            sp.GetRequiredService<AppDbContext>(),
            new FixedClock(Now),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<CompanyWatchScanJob>());
        await job.RunAsync(ct);
    }

    private async Task<IReadOnlyList<FollowedCompanyAdHit>> GetHitsAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FollowedCompanyAdHits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(h => h.UserId == userId)
            .ToListAsync(ct);
    }

    private async Task<JobSeeker> GetSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.JobSeekers.AsNoTracking().FirstOrDefaultAsync(js => js.UserId == userId, ct))!;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
