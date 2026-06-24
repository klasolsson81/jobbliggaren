using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Jobs.BackgroundMatching;

/// <summary>
/// ADR 0080 Vag 4 PR-3 — UNIT cover for the load-bearing OPERATIONAL invariants of the
/// background-matching scan that the 7 integration tests (happy paths) do not pin:
/// per-user failure ISOLATION (one user's exception must not abort the batch and must NOT
/// advance that user's watermark) and that the per-user catch EXCLUDES cancellation
/// (OperationCanceledException propagates — host shutdown / cron-timeout stops the scan
/// promptly, never mis-logged as a user failure). Mirrors <c>HardDeleteAccountsJobTests</c>
/// (the TD-25 resilient-loop precedent). The collaborators (IMatchProfileBuilder, IMatchScorer)
/// are NSubstitute mocks; the IAppDbContext is the established real-AppDbContext-over-EF-InMemory
/// fake (TestAppDbContextFactory) so the multi-set async query (db.JobSeekers.Where(consent)
/// .Select(UserId).ToListAsync + the per-user FirstOrDefaultAsync + the watermark read-back) runs
/// as a genuine IQueryable — parity <c>DetectGhostedApplicationsJobTests</c>.
/// </summary>
public class BackgroundMatchingJobTests
{
    private static readonly FakeDateTimeProvider NowClock =
        new(new DateTimeOffset(2026, 6, 24, 3, 20, 0, TimeSpan.Zero));

    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IMatchScorer _scorer = Substitute.For<IMatchScorer>();

    // PR-4b added two ctor params (IEmailSender + IUserAccountService) for the Top-direct hook.
    // These existing scan-invariant tests do not seed Top matches (the builder is mocked to gate or
    // produce no ads), so the email collaborators are never exercised — a default IUserAccountService
    // whose GetEmailAsync returns a non-blank address keeps the (unreached) dispatch path benign.
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUserAccountService _userAccounts = Substitute.For<IUserAccountService>();

    public BackgroundMatchingJobTests()
    {
        _userAccounts.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("seeker@example.com");
    }

    private BackgroundMatchingJob CreateJob(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        IMatchProfileBuilder profileBuilder,
        IMatchScorer scorer) =>
        new(db, profileBuilder, scorer, _emailSender, _userAccounts, NowClock,
            NullLogger<BackgroundMatchingJob>.Instance);

    // Seeds a CONSENTING JobSeeker (opt-in ON, not withdrawn) and returns its UserId. The
    // produced profile is mocked per-test, so the seeker's MatchPreferences are irrelevant to
    // the SSYK gate — what matters here is only that the consent filter selects this row.
    private static async Task<Guid> SeedConsentingSeekerAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, "Test", NowClock).Value;
        // Opt-in ON, never withdrawn → inside the consenting set.
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, NowClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // A valid FULL profile with a STATED occupation (non-empty SSYK) → passes the SSYK gate.
    private static FullCandidateMatchProfile ProfileWithOccupation() =>
        new(new CandidateMatchProfile(
                Title: "Systemutvecklare",
                SsykGroupConceptIds: ["2512"],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    // The honest EMPTY-SSYK profile → hits the SSYK gate (advance watermark, 0 matches, no
    // scorer call).
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

    // 1. FAILURE ISOLATION (the Major). Two consenting users; userA's profile build THROWS a
    // non-cancellation exception. The batch must NOT abort: userB is still processed (its build
    // ran + its watermark advanced) and userA's watermark must NOT advance (the throw happens
    // before the per-user SaveChanges, so nothing commits for the failed scan).
    [Fact]
    public async Task RunAsync_OneUserThrows_IsolatesFailureAndContinuesBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userA = await SeedConsentingSeekerAsync(db, ct);
        var userB = await SeedConsentingSeekerAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userA, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("byggfel för userA"));
        _profileBuilder.BuildFullForUserIdAsync(userB, Arg.Any<CancellationToken>())
            .Returns(ProfileWithoutOccupation()); // userB hits the SSYK gate → advances, 0 matches

        var job = CreateJob(db, _profileBuilder, _scorer);

        // The batch must not throw — userA's failure is caught + logged, not propagated.
        await Should.NotThrowAsync(() => job.RunAsync(ct));

        // userB was still processed past the failing userA.
        await _profileBuilder.Received(1)
            .BuildFullForUserIdAsync(userB, Arg.Any<CancellationToken>());

        // userA's watermark did NOT advance (throw before the per-user SaveChanges → rollback).
        var reloadedA = await ReloadSeekerAsync(db, userA, ct);
        reloadedA.LastMatchScanAt.ShouldBeNull();

        // userB's watermark DID advance (its scan committed independently of userA's failure).
        var reloadedB = await ReloadSeekerAsync(db, userB, ct);
        reloadedB.LastMatchScanAt.ShouldBe(NowClock.UtcNow);
    }

    // 1b. The failing user's scan does NOT commit (state-level restatement of the isolation
    // invariant): a single failing consenting user leaves its watermark null and the batch
    // completes cleanly.
    [Fact]
    public async Task RunAsync_SoleUserThrows_DoesNotCommitWatermarkAndDoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userA = await SeedConsentingSeekerAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userA, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("byggfel"));

        var job = CreateJob(db, _profileBuilder, _scorer);

        await Should.NotThrowAsync(() => job.RunAsync(ct));

        var reloaded = await ReloadSeekerAsync(db, userA, ct);
        reloaded.LastMatchScanAt.ShouldBeNull();
    }

    // 2. The per-user catch EXCLUDES cancellation: an OperationCanceledException from the build
    // is NOT swallowed (catch ... when (ex is not OperationCanceledException)) — it propagates so
    // host shutdown / cron-timeout stops the scan promptly.
    [Fact]
    public async Task RunAsync_BuildThrowsOperationCanceled_PropagatesNotSwallowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userA = await SeedConsentingSeekerAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userA, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var job = CreateJob(db, _profileBuilder, _scorer);

        await Should.ThrowAsync<OperationCanceledException>(() => job.RunAsync(ct));

        // The cancellation aborted the scan before any commit → watermark unchanged.
        var reloaded = await ReloadSeekerAsync(db, userA, ct);
        reloaded.LastMatchScanAt.ShouldBeNull();
    }

    // 2b. A pre-cancelled token aborts the batch at the per-user ThrowIfCancellationRequested
    // before any profile is built — parity DetectGhostedApplicationsJobTests cancellation cover.
    [Fact]
    public async Task RunAsync_PreCancelledToken_ThrowsAndBuildsNoProfile()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedConsentingSeekerAsync(db, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var job = CreateJob(db, _profileBuilder, _scorer);

        await Should.ThrowAsync<OperationCanceledException>(() => job.RunAsync(cts.Token));

        await _profileBuilder.DidNotReceiveWithAnyArgs()
            .BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // 3. EMPTY consenting set: no opt-in users → the scan completes, builds no profile, and
    // never touches the scorer. (A non-consenting seeker is seeded to prove the consent filter
    // excludes it.)
    [Fact]
    public async Task RunAsync_NoConsentingUsers_CompletesWithoutBuildOrScore()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        // Default Preferences → BackgroundMatchNotificationsEnabled == false → excluded.
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Ej samtyckande", NowClock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var job = CreateJob(db, _profileBuilder, _scorer);

        await Should.NotThrowAsync(() => job.RunAsync(ct));

        await _profileBuilder.DidNotReceiveWithAnyArgs()
            .BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _scorer.DidNotReceiveWithAnyArgs().ScoreFullBatchAsync(
            Arg.Any<IReadOnlyList<JobAdId>>(),
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<CancellationToken>());
    }

    // 3b. A WITHDRAWN consent (opt-in toggled off → NotificationConsentWithdrawnAt stamped) is
    // excluded by the consent filter even though it was once enabled (GDPR Art. 7(3) — withdrawal
    // stops dispatch immediately).
    [Fact]
    public async Task RunAsync_WithdrawnConsent_IsExcludedFromScan()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Återkallat", NowClock).Value;
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, NowClock);
        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Weekly, NowClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var job = CreateJob(db, _profileBuilder, _scorer);

        await Should.NotThrowAsync(() => job.RunAsync(ct));

        await _profileBuilder.DidNotReceiveWithAnyArgs()
            .BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // 4. SSYK GATE: a consenting user whose profile has an EMPTY SSYK set is gated BEFORE
    // scoring — the scorer is never called, yet the watermark still advances (one SaveChanges,
    // 0 matches) so the user is not re-scanned next run.
    [Fact]
    public async Task RunAsync_EmptySsyk_SkipsScorerButAdvancesWatermark()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithoutOccupation());

        var job = CreateJob(db, _profileBuilder, _scorer);

        await Should.NotThrowAsync(() => job.RunAsync(ct));

        // Gated before scoring — the scorer is never reached.
        await _scorer.DidNotReceiveWithAnyArgs().ScoreFullBatchAsync(
            Arg.Any<IReadOnlyList<JobAdId>>(),
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<CancellationToken>());

        // But the watermark advances (scanned through now, 0 matches) so we do not re-scan.
        var reloaded = await ReloadSeekerAsync(db, userId, ct);
        reloaded.LastMatchScanAt.ShouldBe(NowClock.UtcNow);
    }

    // 4b. A stated-occupation user with no NEW ads (cold-start window yields an empty ad set)
    // still advances the watermark without invoking the scorer — proving the advance is
    // unconditional on a successful scan, the idempotency spine the integration happy-paths lean
    // on. (No JobAds are seeded → the CreatedAt > since filter returns empty → newAdIds.Count ==
    // 0 → scorer skipped, watermark advanced in the same SaveChanges.)
    [Fact]
    public async Task RunAsync_StatedOccupationNoNewAds_AdvancesWatermarkWithoutScoring()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());

        var job = CreateJob(db, _profileBuilder, _scorer);

        await Should.NotThrowAsync(() => job.RunAsync(ct));

        await _scorer.DidNotReceiveWithAnyArgs().ScoreFullBatchAsync(
            Arg.Any<IReadOnlyList<JobAdId>>(),
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<CancellationToken>());

        var reloaded = await ReloadSeekerAsync(db, userId, ct);
        reloaded.LastMatchScanAt.ShouldBe(NowClock.UtcNow);
    }
}
