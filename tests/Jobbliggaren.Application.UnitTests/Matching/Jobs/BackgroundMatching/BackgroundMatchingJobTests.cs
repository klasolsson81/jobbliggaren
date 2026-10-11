using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Jobs.BackgroundMatching;

/// <summary>
/// ADR 0080 Vag 4, ADR 0161 (#2106) — UNIT cover for the scan phase of the background-matching run:
/// per-user failure isolation, cancellation, the consent filter, the SSYK gate, the watermark
/// (trailing the run clock by <see cref="BackgroundMatchingJob.IngestSettleWindow"/>, floored at
/// <see cref="BackgroundMatchingJob.ColdStartDays"/>), the empty-slice skip, the scan budget, the user
/// order and the scope per user. The send phase is <see cref="TopMatchMailDispatcherTests"/>; the scan
/// and send together are <see cref="BackgroundMatchingJobTopDirectTests"/>. The profile builder and
/// scorer are NSubstitute mocks; the context is a real AppDbContext over EF InMemory
/// (TestAppDbContextFactory), parity <c>HardDeleteAccountsJobTests</c>.
/// </summary>
public class BackgroundMatchingJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 3, 22, 0, TimeSpan.Zero);
    private static readonly FakeDateTimeProvider NowClock = new(Now);

    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IMatchScorer _scorer = Substitute.For<IMatchScorer>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUserAccountService _userAccounts = Substitute.For<IUserAccountService>();

    public BackgroundMatchingJobTests()
    {
        _userAccounts.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("seeker@example.com");
        _scorer.ScoreFullBatchAsync(
                Arg.Any<IReadOnlyList<JobAdId>>(), Arg.Any<FullCandidateMatchProfile>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<JobAdId, FullScoredMatch>());
    }

    private (BackgroundMatchingJob Job, FakeMatchingScopeFactory Scopes) Compose(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, IDateTimeProvider? clock = null)
    {
        clock ??= NowClock;
        var scopes = new FakeMatchingScopeFactory(db, _profileBuilder, _scorer, _userAccounts);
        var dispatcher = new TopMatchMailDispatcher(
            scopes, _emailSender, clock, NullLogger<TopMatchMailDispatcher>.Instance);
        return (new BackgroundMatchingJob(scopes, dispatcher, clock, NullLogger<BackgroundMatchingJob>.Instance),
            scopes);
    }

    private BackgroundMatchingJob CreateJob(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, IDateTimeProvider? clock = null) =>
        Compose(db, clock).Job;

    // A CONSENTING JobSeeker (opt-in ON, never withdrawn). The profile is mocked per test, so the
    // seeker's MatchPreferences do not matter here; only that the consent filter selects the row.
    private static async Task<Guid> SeedConsentingSeekerAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(NowClock), NowClock).Value;
        seeker.UpdateNotificationConsent(enabled: true, NowClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // A consenting seeker an earlier run already scanned: the watermark is the one that run's own
    // AdvanceMatchScan call wrote (its run clock minus IngestSettleWindow).
    private static async Task<Guid> SeedSeekerScannedAtAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, DateTimeOffset earlierRunAt, CancellationToken ct)
    {
        var earlierRun = new FakeDateTimeProvider(earlierRunAt);
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(earlierRun), earlierRun).Value;
        seeker.UpdateNotificationConsent(enabled: true, earlierRun);
        seeker.AdvanceMatchScan(earlierRunAt - BackgroundMatchingJob.IngestSettleWindow, earlierRun);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // An Active ad ingested at `ingestedAt` (JobAd stamps CreatedAt from its clock).
    private static async Task<JobAdId> SeedActiveAdAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, DateTimeOffset ingestedAt, CancellationToken ct)
    {
        var ingestClock = new FakeDateTimeProvider(ingestedAt);
        var jobAd = JobAd.Create(
            "Systemutvecklare", Company.Create("Acme AB").Value, "Beskrivning", "https://example.com/jobb",
            JobSource.Platsbanken, ingestClock.UtcNow, ingestClock.UtcNow.AddDays(30), ingestClock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private static Task<JobAdId> SeedActiveAdAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, CancellationToken ct) =>
        SeedActiveAdAsync(db, Now.AddHours(-1), ct);

    // A valid FULL profile with a STATED occupation (non-empty SSYK) → passes the SSYK gate.
    private static FullCandidateMatchProfile ProfileWithOccupation() =>
        new(new CandidateMatchProfile(
                Title: "Systemutvecklare",
                SsykGroupConceptIds: ["2512"],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    // The honest EMPTY-SSYK profile → hits the SSYK gate (advance watermark, 0 matches, no scorer call).
    private static FullCandidateMatchProfile ProfileWithoutOccupation() =>
        new(new CandidateMatchProfile(
                Title: "",
                SsykGroupConceptIds: [],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    private static async Task<JobSeeker> ReloadSeekerAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, CancellationToken ct) =>
        await db.JobSeekers.AsNoTracking().FirstAsync(js => js.UserId == userId, ct);

    private static DateTimeOffset ScannedThrough(DateTimeOffset runAt) =>
        runAt - BackgroundMatchingJob.IngestSettleWindow;

    // ───────────────────────────── Isolation and cancellation

    // One user's build throws: the run does not abort, the next user is still scanned, and only the
    // failed user's watermark stays where it was.
    [Fact]
    public async Task RunAsync_OneUserThrows_IsolatesFailureAndContinuesBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userA = await SeedConsentingSeekerAsync(db, ct);
        var userB = await SeedConsentingSeekerAsync(db, ct);
        await SeedActiveAdAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userA, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("byggfel för userA"));
        _profileBuilder.BuildFullForUserIdAsync(userB, Arg.Any<CancellationToken>())
            .Returns(ProfileWithoutOccupation());

        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        await _profileBuilder.Received(1).BuildFullForUserIdAsync(userB, Arg.Any<CancellationToken>());
        (await ReloadSeekerAsync(db, userA, ct)).LastMatchScanAt.ShouldBeNull();
        (await ReloadSeekerAsync(db, userB, ct)).LastMatchScanAt.ShouldBe(ScannedThrough(Now));
    }

    [Fact]
    public async Task RunAsync_SoleUserThrows_DoesNotCommitWatermarkAndDoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userA = await SeedConsentingSeekerAsync(db, ct);
        await SeedActiveAdAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userA, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("byggfel"));

        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        (await ReloadSeekerAsync(db, userA, ct)).LastMatchScanAt.ShouldBeNull();
    }

    // The per-user catch excludes cancellation, so a host shutdown stops the run promptly.
    [Fact]
    public async Task RunAsync_BuildThrowsOperationCanceled_PropagatesNotSwallowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userA = await SeedConsentingSeekerAsync(db, ct);
        await SeedActiveAdAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userA, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => CreateJob(db).RunAsync(ct));

        (await ReloadSeekerAsync(db, userA, ct)).LastMatchScanAt.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_PreCancelledToken_ThrowsAndBuildsNoProfile()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedConsentingSeekerAsync(db, CancellationToken.None);
        await SeedActiveAdAsync(db, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => CreateJob(db).RunAsync(cts.Token));

        await _profileBuilder.DidNotReceiveWithAnyArgs()
            .BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────── Consent

    // A non-consenting seeker is excluded, so nothing is built or scored even with a new ad present.
    [Fact]
    public async Task RunAsync_NoConsentingUsers_CompletesWithoutBuildOrScore()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(NowClock), NowClock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        await SeedActiveAdAsync(db, ct);

        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        await _profileBuilder.DidNotReceiveWithAnyArgs()
            .BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _scorer.DidNotReceiveWithAnyArgs().ScoreFullBatchAsync(
            Arg.Any<IReadOnlyList<JobAdId>>(), Arg.Any<FullCandidateMatchProfile>(), Arg.Any<CancellationToken>());
    }

    // A withdrawn consent (on, then off) is excluded although it was once enabled (GDPR Art. 7(3)).
    [Fact]
    public async Task RunAsync_WithdrawnConsent_IsExcludedFromScan()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(NowClock), NowClock).Value;
        seeker.UpdateNotificationConsent(enabled: true, NowClock);
        seeker.UpdateNotificationConsent(enabled: false, NowClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        await SeedActiveAdAsync(db, ct);

        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        await _profileBuilder.DidNotReceiveWithAnyArgs()
            .BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────── The watermark

    // SSYK gate: no stated occupation, so the scorer is never reached, yet the watermark advances in
    // the gate branch too, to the run clock minus the settle window.
    [Fact]
    public async Task RunAsync_EmptySsyk_SkipsScorerButAdvancesWatermarkToNowMinusTheSettleWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        await SeedActiveAdAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithoutOccupation());

        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        await _scorer.DidNotReceiveWithAnyArgs().ScoreFullBatchAsync(
            Arg.Any<IReadOnlyList<JobAdId>>(), Arg.Any<FullCandidateMatchProfile>(), Arg.Any<CancellationToken>());
        (await ReloadSeekerAsync(db, userId, ct)).LastMatchScanAt.ShouldBe(ScannedThrough(Now));
    }

    // The scored branch advances to the same point: never to "now", which would skip an ad stamped
    // before this run's read and committed after it.
    [Fact]
    public async Task RunAsync_ScoredUser_AdvancesWatermarkToNowMinusTheSettleWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        await SeedActiveAdAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());

        await CreateJob(db).RunAsync(ct);

        await _scorer.Received(1).ScoreFullBatchAsync(
            Arg.Any<IReadOnlyList<JobAdId>>(), Arg.Any<FullCandidateMatchProfile>(), Arg.Any<CancellationToken>());
        (await ReloadSeekerAsync(db, userId, ct)).LastMatchScanAt.ShouldBe(ScannedThrough(Now));
    }

    // No ad is newer than the user's floor: the user is skipped entirely. No profile build, no score,
    // no write, and the watermark stays unset (security-auditor V6).
    [Fact]
    public async Task RunAsync_NoNewAds_SkipsTheUserWithoutAProfileBuildOrAWrite()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var saves = 0;
        db.SavingChanges += (_, _) => saves++;

        await CreateJob(db).RunAsync(ct);

        await _profileBuilder.DidNotReceiveWithAnyArgs()
            .BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _scorer.DidNotReceiveWithAnyArgs().ScoreFullBatchAsync(
            Arg.Any<IReadOnlyList<JobAdId>>(), Arg.Any<FullCandidateMatchProfile>(), Arg.Any<CancellationToken>());
        saves.ShouldBe(0, "a run with nothing new for anyone writes nothing");
        (await ReloadSeekerAsync(db, userId, ct)).LastMatchScanAt.ShouldBeNull();
    }

    // The run's candidate read is not empty (another user has an ad in their slice), but nothing is
    // newer than THIS user's watermark: this user is skipped, their row untouched.
    [Fact]
    public async Task RunAsync_UserWhoseSliceIsEmpty_IsNotScanned_AndTheirRowIsNotWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        await SeedActiveAdAsync(db, Now.AddHours(-1), ct);
        var steady = await SeedSeekerScannedAtAsync(db, Now.AddMinutes(-10), ct);
        var neverScanned = await SeedConsentingSeekerAsync(db, ct);
        var before = await ReloadSeekerAsync(db, steady, ct);

        _profileBuilder.BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());

        await CreateJob(db).RunAsync(ct);

        await _profileBuilder.DidNotReceive().BuildFullForUserIdAsync(steady, Arg.Any<CancellationToken>());
        await _profileBuilder.Received(1).BuildFullForUserIdAsync(neverScanned, Arg.Any<CancellationToken>());
        var after = await ReloadSeekerAsync(db, steady, ct);
        after.LastMatchScanAt.ShouldBe(before.LastMatchScanAt);
        after.UpdatedAt.ShouldBe(before.UpdatedAt);
    }

    // D11: a user who withdrew and re-consented after more than ColdStartDays is scanned from the
    // cold-start floor, never from the old watermark. The watermark is written by an earlier real run;
    // the pause and the re-consent go through JobSeeker.UpdateNotificationConsent and the clock.
    [Fact]
    public async Task RunAsync_ReConsentAfterMoreThanColdStartDays_ScansOnlyTheColdStartWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var t0 = Now;
        var clock = new MutableClock(t0);
        var userId = await SeedConsentingSeekerAsync(db, ct);
        await SeedActiveAdAsync(db, t0.AddHours(-1), ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithoutOccupation());
        await CreateJob(db, clock).RunAsync(ct);
        (await ReloadSeekerAsync(db, userId, ct)).LastMatchScanAt.ShouldBe(ScannedThrough(t0));

        var seeker = await db.JobSeekers.FirstAsync(js => js.UserId == userId, ct);
        seeker.UpdateNotificationConsent(enabled: false, new FakeDateTimeProvider(t0.AddDays(1)));
        await db.SaveChangesAsync(ct);

        var outsideWindow = await SeedActiveAdAsync(db, t0.AddDays(2), ct);
        var insideWindow = await SeedActiveAdAsync(db, t0.AddDays(9), ct);

        var reConsentAt = t0.AddDays(BackgroundMatchingJob.ColdStartDays + 3);
        seeker.UpdateNotificationConsent(enabled: true, new FakeDateTimeProvider(reConsentAt));
        await db.SaveChangesAsync(ct);

        IReadOnlyList<JobAdId>? scored = null;
        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        _scorer.ScoreFullBatchAsync(
                Arg.Do<IReadOnlyList<JobAdId>>(ids => scored = ids),
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<JobAdId, FullScoredMatch>());

        clock.UtcNow = reConsentAt;
        await CreateJob(db, clock).RunAsync(ct);

        scored.ShouldNotBeNull();
        scored.ShouldBe([insideWindow],
            "the scan after a re-consent reaches back ColdStartDays, not to the watermark of the old consent");
        scored.ShouldNotContain(outsideWindow);
        (await ReloadSeekerAsync(db, userId, ct)).LastMatchScanAt.ShouldBe(ScannedThrough(reConsentAt));
    }

    // ───────────────────────────── Budget and order

    // The first user's scan outlasts ScanBudget (the clock moves while the profile is built): the run
    // starts no further user, and that user's watermark stays for the next run.
    [Fact]
    public async Task RunAsync_ScanBudgetSpent_StopsStartingUsers_AndLeavesTheirWatermark()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var clock = new MutableClock(Now);
        await SeedActiveAdAsync(db, Now.AddMinutes(-1), ct);
        var first = await SeedSeekerScannedAtAsync(db, Now.AddMinutes(-30), ct);
        var second = await SeedSeekerScannedAtAsync(db, Now.AddMinutes(-10), ct);
        var secondBefore = await ReloadSeekerAsync(db, second, ct);

        _profileBuilder.BuildFullForUserIdAsync(first, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                clock.Advance(BackgroundMatchingJob.ScanBudget);
                return ProfileWithoutOccupation();
            });

        await CreateJob(db, clock).RunAsync(ct);

        (await ReloadSeekerAsync(db, first, ct)).LastMatchScanAt.ShouldBe(ScannedThrough(Now));
        await _profileBuilder.DidNotReceive().BuildFullForUserIdAsync(second, Arg.Any<CancellationToken>());
        (await ReloadSeekerAsync(db, second, ct)).LastMatchScanAt.ShouldBe(secondBefore.LastMatchScanAt);
    }

    // Steady users first, oldest watermark first; never-scanned users last. Seeded in the reverse
    // order, so an unordered read, or an order that sorts nulls first, fails.
    [Fact]
    public async Task RunAsync_OrdersSteadyUsersByWatermark_AndNeverScannedUsersLast()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        await SeedActiveAdAsync(db, Now.AddMinutes(-1), ct);
        var neverScanned = await SeedConsentingSeekerAsync(db, ct);
        var recent = await SeedSeekerScannedAtAsync(db, Now.AddMinutes(-10), ct);
        var oldest = await SeedSeekerScannedAtAsync(db, Now.AddMinutes(-30), ct);

        var order = new List<Guid>();
        _profileBuilder.BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                order.Add(call.Arg<Guid>());
                return ProfileWithoutOccupation();
            });

        await CreateJob(db).RunAsync(ct);

        order.ShouldBe([oldest, recent, neverScanned]);
    }

    // ───────────────────────────── Scopes

    // #751: one short scope for the run's reads, a FRESH child scope per scanned user (its own change
    // tracker), and one scope for the send phase. Hoisting the per-user scope out of the loop gives 3.
    [Fact]
    public async Task RunAsync_CreatesOneChildScopePerUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userA = await SeedConsentingSeekerAsync(db, ct);
        var userB = await SeedConsentingSeekerAsync(db, ct);
        await SeedActiveAdAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userA, Arg.Any<CancellationToken>())
            .Returns(ProfileWithoutOccupation());
        _profileBuilder.BuildFullForUserIdAsync(userB, Arg.Any<CancellationToken>())
            .Returns(ProfileWithoutOccupation());

        var (job, scopes) = Compose(db);

        await Should.NotThrowAsync(() => job.RunAsync(ct));

        scopes.ScopesCreated.ShouldBe(4,
            "1 scope for the run's reads + 1 child scope per scanned user (2) + 1 for the send phase = 4; "
            + "one shared scope for every user gives 3 (#751 poison isolation)");
    }
}
