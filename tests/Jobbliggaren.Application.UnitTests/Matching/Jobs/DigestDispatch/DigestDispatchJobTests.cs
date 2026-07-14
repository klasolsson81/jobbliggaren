using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Jobs.DigestDispatch;

/// <summary>
/// ADR 0080 Vag 4 PR-4b — UNIT cover for the Strong-match digest dispatch. No scorer/engine: the
/// matches are seeded directly as <see cref="UserJobAdMatch"/> rows + a consenting
/// <see cref="JobSeeker"/> + the joined <see cref="JobAd"/>s, then the job composes ONE cadence email
/// and drains the rows. The IAppDbContext is the real-AppDbContext-over-EF-InMemory fake
/// (TestAppDbContextFactory) so the consent predicate, the Grade+Pending filter, the JobAd JOIN, the
/// recency ordering and the cap all run as a genuine IQueryable — parity
/// <see cref="BackgroundMatching.BackgroundMatchingJobTests"/>.
/// <para>
/// Invariants pinned: cadence routing (a Weekly user is invisible to a Daily run and vice versa);
/// the Grade filter (only Strong is digested — Good/Top are excluded); the claim-then-drain Sent
/// transition; the anti-spam cap (body lists ≤ MaxItemsPerDigest while TotalCount is honest, yet ALL
/// pending rows drain); consent gates (off/withdrawn → no email); failure paths (send throws → rows
/// left Queued; no account email → no email, rows Queued); the empty-window short-circuit (no pending
/// → no send); per-user isolation; and recency ordering of the displayed items.
/// </para>
/// </summary>
public class DigestDispatchJobTests
{
    private static readonly FakeDateTimeProvider NowClock =
        new(new DateTimeOffset(2026, 6, 24, 6, 0, 0, TimeSpan.Zero));

    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUserAccountService _userAccounts = Substitute.For<IUserAccountService>();

    // The per-watch grade filter ports (bevakning F3). These stay UNTOUCHED in the Strong-match +
    // no-filter follow tests here (the grade path is dormant unless a watch has OnlyMatched set) — the
    // real ≥Good SQL + the filter narrowing are pinned end-to-end in FollowedCompanyDigestIntegrationTests
    // (Testcontainers). A test that needs the filter configures these substitutes explicitly.
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

    private const string ToEmail = "seeker@example.com";

    public DigestDispatchJobTests()
    {
        _userAccounts.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ToEmail);
    }

    private DigestDispatchJob CreateJob(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, int maxItems = 20) =>
        new(db, _emailSender, _userAccounts, _profileBuilder, _perUserSearch, NowClock,
            Options.Create(new DigestDispatchOptions { MaxItemsPerDigest = maxItems }),
            NullLogger<DigestDispatchJob>.Instance);

    // ───────────────────────────── Seeding

    // A consenting JobSeeker with the chosen cadence (opt-in ON, never withdrawn).
    private static async Task<Guid> SeedConsentingSeekerAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, DigestCadence cadence,
        CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, "Test", NowClock).Value;
        seeker.UpdateNotificationConsent(enabled: true, cadence, NowClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // A JobSeeker that opted in then WITHDREW → BackgroundMatchNotificationsEnabled=false AND
    // NotificationConsentWithdrawnAt set (the due-set query excludes it on either predicate).
    private static async Task<Guid> SeedWithdrawnSeekerAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, DigestCadence cadence,
        CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, "Återkallat", NowClock).Value;
        seeker.UpdateNotificationConsent(enabled: true, cadence, NowClock);
        seeker.UpdateNotificationConsent(enabled: false, cadence, NowClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    private static async Task<JobAdId> SeedActiveAdAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        string title, string company, CancellationToken ct)
    {
        var jobAd = JobAd.Create(
            title, Company.Create(company).Value, "Beskrivning", "https://example.com/jobb",
            JobSource.Platsbanken, NowClock.UtcNow, NowClock.UtcNow.AddDays(30), NowClock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // Seeds a Pending match of the given grade for (userId, ad). createdAt lets a test control the
    // recency ordering (the digest orders by CreatedAt desc); defaults to NowClock.
    private static async Task<JobAdId> SeedMatchAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId, NotifiableMatchGrade grade, string title, string company,
        CancellationToken ct, DateTimeOffset? createdAt = null)
    {
        var adId = await SeedActiveAdAsync(db, title, company, ct);
        var clock = new ClockAt(createdAt ?? NowClock.UtcNow);
        var match = UserJobAdMatch.Create(userId, adId, grade, [], clock).Value;
        db.UserJobAdMatches.Add(match);
        await db.SaveChangesAsync(ct);
        return adId;
    }

    private static async Task<UserJobAdMatch?> ReloadMatchAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId, JobAdId jobAdId, CancellationToken ct) =>
        await db.UserJobAdMatches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.JobAdId == jobAdId, ct);

    // ───────────────────────────── 1. Weekly user with Strong matches → one Digest email, rows Sent

    [Fact]
    public async Task RunAsync_WeeklyUserWithStrongMatches_SendsDigest_AndMarksRowsSent()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        // Stagger CreatedAt so the recency sort is fully determined by CreatedAt alone — EF InMemory
        // cannot compare the strongly-typed Id VO when the ThenBy(Id) tiebreaker is actually invoked
        // (tied timestamps); real Postgres orders the uuid column fine, so this is a test-infra
        // constraint, not a production limit (parity the staggered cap/ordering tests below).
        var adA = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Roll A", "Bolag A", ct,
            createdAt: NowClock.UtcNow);
        var adB = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Roll B", "Bolag B", ct,
            createdAt: NowClock.UtcNow.AddMinutes(-1));

        MatchNotificationEmail? captured = null;
        await _emailSender.SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<MatchNotificationEmail>(c => captured = c),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        await _emailSender.Received(1).SendMatchNotificationEmailAsync(
            ToEmail, Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(MatchNotificationKind.Digest);
        captured.Cadence.ShouldBe(DigestCadence.Weekly);
        captured.TotalCount.ShouldBe(2);
        captured.Items.Count.ShouldBe(2);
        captured.Items.ShouldAllBe(i => i.GradeLabel == "Stark match");

        (await ReloadMatchAsync(db, userId, adA, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent);
        (await ReloadMatchAsync(db, userId, adB, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent);
    }

    // ───────────────────────────── 2. Only Strong is digested — Good/Top are not

    [Fact]
    public async Task RunAsync_OnlyStrongIsDigested_GoodAndTopAreIgnored()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var strongAd = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Stark", "S AB", ct);
        var goodAd = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Good, "Bra", "G AB", ct);
        var topAd = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Top, "Topp", "T AB", ct);

        MatchNotificationEmail? captured = null;
        await _emailSender.SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<MatchNotificationEmail>(c => captured = c),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        // The email reflects ONLY the Strong row.
        captured.ShouldNotBeNull();
        captured.TotalCount.ShouldBe(1, "endast Strong digesteras");
        captured.Items.Count.ShouldBe(1);

        // Only the Strong row is drained; Good + Top stay Pending (Good=in-app, Top=direct-dispatched).
        (await ReloadMatchAsync(db, userId, strongAd, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent);
        (await ReloadMatchAsync(db, userId, goodAd, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Pending, "Good ska ej röras av digesten");
        (await ReloadMatchAsync(db, userId, topAd, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Pending, "Top ska ej röras av digesten");
    }

    // ───────────────────────────── 3. Cadence routing — Weekly user invisible to a Daily run

    [Fact]
    public async Task RunAsync_DailyRun_DoesNotProcessWeeklyUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var weeklyUser = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        await SeedMatchAsync(db, weeklyUser, NotifiableMatchGrade.Strong, "Roll", "Bolag", ct);

        await CreateJob(db).RunAsync(DigestCadence.Daily, ct);

        // The cron IS the window: a Daily run dispatches only Daily users.
        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WeeklyRun_DoesNotProcessDailyUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var dailyUser = await SeedConsentingSeekerAsync(db, DigestCadence.Daily, ct);
        await SeedMatchAsync(db, dailyUser, NotifiableMatchGrade.Strong, "Roll", "Bolag", ct);

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────── 4. Consent gates — withdrawn / opt-in OFF → no email

    [Fact]
    public async Task RunAsync_WithdrawnConsent_DoesNotEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedWithdrawnSeekerAsync(db, DigestCadence.Weekly, ct);
        await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Roll", "Bolag", ct);

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OptInOff_DoesNotEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        // Default Preferences → BackgroundMatchNotificationsEnabled == false (never opted in).
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, "Ej samtyckande", NowClock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Roll", "Bolag", ct);

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────── 5. Cap — body lists ≤ cap, TotalCount honest, ALL rows drain

    [Fact]
    public async Task RunAsync_MoreStrongThanCap_CapsDisplayedItems_ButDrainsAllPending()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);

        const int cap = 3;
        const int total = 5;
        var adIds = new List<JobAdId>();
        for (var i = 0; i < total; i++)
        {
            // Stagger CreatedAt so the ordering is well-defined and the cap is deterministic.
            adIds.Add(await SeedMatchAsync(
                db, userId, NotifiableMatchGrade.Strong, $"Roll {i}", $"Bolag {i}", ct,
                createdAt: NowClock.UtcNow.AddMinutes(-i)));
        }

        MatchNotificationEmail? captured = null;
        await _emailSender.SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<MatchNotificationEmail>(c => captured = c),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db, maxItems: cap).RunAsync(DigestCadence.Weekly, ct);

        captured.ShouldNotBeNull();
        captured.Items.Count.ShouldBe(cap, "kroppen listar högst MaxItemsPerDigest");
        captured.TotalCount.ShouldBe(total, "TotalCount är det ärliga fönstret (för 'och N till')");

        // ALL window rows drain (not just the displayed cap) — the un-displayed remainder must not
        // re-surface next digest.
        foreach (var adId in adIds)
        {
            (await ReloadMatchAsync(db, userId, adId, ct))!.NotificationStatus
                .ShouldBe(NotificationStatus.Sent,
                    "alla fönster-rader ska dräneras (Sent), inte bara de visade");
        }
    }

    // ───────────────────────────── 6. Send throws → rows left Queued, no throw

    [Fact]
    public async Task RunAsync_SendThrows_LeavesRowsQueued_AndDoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        // Stagger CreatedAt — see RunAsync_WeeklyUserWithStrongMatches_SendsDigest for why the
        // ThenBy(Id) tiebreaker must not be exercised under EF InMemory.
        var adA = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Roll A", "Bolag A", ct,
            createdAt: NowClock.UtcNow);
        var adB = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Roll B", "Bolag B", ct,
            createdAt: NowClock.UtcNow.AddMinutes(-1));

        _emailSender.SendMatchNotificationEmailAsync(
                Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mejlleverans nere"));

        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(DigestCadence.Weekly, ct));

        // Claimed before the send → both rows stay Queued (never re-sent; TD-114).
        (await ReloadMatchAsync(db, userId, adA, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Queued);
        (await ReloadMatchAsync(db, userId, adB, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Queued);
    }

    // ───────────────────────────── 7. No account email → no send, rows Queued

    [Fact]
    public async Task RunAsync_NoAccountEmail_DoesNotEmail_AndRowsLeftQueued()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var adId = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Roll", "Bolag", ct);
        _userAccounts.GetEmailAsync(userId, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        // The rows were claimed (Queued) before the email resolve; no recipient → they stay Queued.
        (await ReloadMatchAsync(db, userId, adId, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Queued);
    }

    // ───────────────────────────── 8. No pending Strong → no email

    [Fact]
    public async Task RunAsync_NoPendingStrong_DoesNotEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        // Consenting user, but no Strong matches (only a Good one, which is not digested).
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        await SeedMatchAsync(db, userId, NotifiableMatchGrade.Good, "Bra", "G AB", ct);

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────── 8b. All matched ads retracted → drain (Sent), no email

    // pending.Count > 0 but the inner JOIN to JobAds yields nothing (every matched ad gone since
    // detection) → DigestDispatchJob drains ALL rows Sent and sends NO email (an empty digest is
    // noise). Without the drain the retracted-ad rows would re-process every digest run forever;
    // without the empty-guard an empty digest would be sent. Both halves are pinned here.
    [Fact]
    public async Task RunAsync_AllMatchedAdsRetracted_DrainsAllRowsSent_AndDoesNotEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);

        // A Strong Pending match whose ad does NOT exist (no JobAd row) — the inner join drops it,
        // so displayRows is empty even though the tracked pending set is not. (No FK — ADR 0058/59.)
        var orphanAdId = JobAdId.New();
        var match = UserJobAdMatch.Create(userId, orphanAdId, NotifiableMatchGrade.Strong, [], NowClock).Value;
        db.UserJobAdMatches.Add(match);
        await db.SaveChangesAsync(ct);

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        (await ReloadMatchAsync(db, userId, orphanAdId, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent,
                "den dränerade raden ska markeras Sent så det tomma fönstret inte om-processas varje körning");
    }

    // ───────────────────────────── 9. Per-user isolation — one user throws, the other still served

    [Fact]
    public async Task RunAsync_OneUserThrows_IsolatesFailure_OtherUserStillGetsDigest()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var failingUser = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        await SeedMatchAsync(db, failingUser, NotifiableMatchGrade.Strong, "Fel", "F AB", ct);
        var okUser = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var okAd = await SeedMatchAsync(db, okUser, NotifiableMatchGrade.Strong, "Ok", "O AB", ct);

        // The failing user's email resolution throws (a per-user fault); the OK user resolves fine.
        _userAccounts.GetEmailAsync(failingUser, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("kontofel"));
        _userAccounts.GetEmailAsync(okUser, Arg.Any<CancellationToken>())
            .Returns(ToEmail);

        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(DigestCadence.Weekly, ct));

        // The OK user still received a digest and its row drained — the fault was isolated.
        await _emailSender.Received(1).SendMatchNotificationEmailAsync(
            ToEmail, Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
        (await ReloadMatchAsync(db, okUser, okAd, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent);
    }

    // ───────────────────────────── 10. Displayed items are ordered most-recent-first

    [Fact]
    public async Task RunAsync_DisplayedItems_AreOrderedMostRecentFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);

        // Seed oldest → newest; the digest must list newest first.
        await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Äldst", "Bolag", ct,
            createdAt: NowClock.UtcNow.AddHours(-3));
        await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Mitten", "Bolag", ct,
            createdAt: NowClock.UtcNow.AddHours(-2));
        await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Nyast", "Bolag", ct,
            createdAt: NowClock.UtcNow.AddHours(-1));

        MatchNotificationEmail? captured = null;
        await _emailSender.SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<MatchNotificationEmail>(c => captured = c),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        captured.ShouldNotBeNull();
        captured.Items.Select(i => i.JobTitle).ShouldBe(["Nyast", "Mitten", "Äldst"]);
    }

    // ───────────────────────────── 11. Digest carries the content-derived idempotency key (#187)

    // The digest send must carry a deterministic, PII-free idempotency key derived from the CONTENT
    // of the claimed Strong set (a hash over the claimed match ids), so a transport retry of the same
    // digest dedupes at Resend while a re-run that claimed a different set yields a different key.
    // Pinned at the call site; the VO's content-derivation + order-independence live in
    // MatchNotificationIdempotencyKeyTests.
    [Fact]
    public async Task RunAsync_Digest_CarriesContentDerivedIdempotencyKey_OverClaimedSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var adA = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Roll A", "Bolag A", ct,
            createdAt: NowClock.UtcNow);
        var adB = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong, "Roll B", "Bolag B", ct,
            createdAt: NowClock.UtcNow.AddMinutes(-1));

        MatchNotificationIdempotencyKey? capturedKey = null;
        await _emailSender.SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Do<MatchNotificationIdempotencyKey>(k => capturedKey = k),
            Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        // The key is derived from the two claimed match ids (order-independent in the VO).
        var matchA = (await ReloadMatchAsync(db, userId, adA, ct))!;
        var matchB = (await ReloadMatchAsync(db, userId, adB, ct))!;
        var key = capturedKey.ShouldNotBeNull();
        key.ShouldBe(MatchNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [matchA.Id.Value, matchB.Id.Value]));
    }

    // ═══════════════════════════ Company-follow pass — per-watch "endast matchade" grade filter (F3)
    //
    // bevakning-reconcile RF-3/RF-5/RF-8=8C (2026-07-12). The read-time grade filter is driven
    // DETERMINISTICALLY here via the substitute IMatchProfileBuilder + IPerUserJobAdSearchQuery — the
    // real ≥Good SQL is pinned separately by F1's FilterToMatchingTests (Testcontainers). These tests
    // pin the WIRING: which hits are claimed/drained vs left Pending, when the fail-fast port is
    // called, and the 13B FilterSummary the email carries.
    //
    // InMemory caveat (verified 2026-07-12): the production follow pass loads the per-watch filter via
    // db.CompanyWatches.AsNoTracking() → the WatchFilterSpec is re-materialised through its jsonb
    // ValueConverter, which the EF-InMemory provider DOES apply, so NeedsGradeCheck sees
    // OnlyMatched==true. If that ever regressed (a filtered-out hit drained instead of staying Pending
    // in RunAsync_OnlyMatchedWatch_AssessableProfile_DrainsMatchingHit_LeavesFilteredOutPending), these
    // would move to FollowedCompanyDigestIntegrationTests (Testcontainers) with a real assessable
    // profile per the architect Test-scope note.

    // A consenting FOLLOW user: the SEPARATE follow-email flag ON (never withdrawn), the shared cadence
    // set via the background-match consent path (which stays OFF, so the match pass never fires here).
    private static async Task<Guid> SeedFollowConsentingSeekerAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, DigestCadence cadence,
        CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, "Follow", NowClock).Value;
        seeker.UpdateNotificationConsent(enabled: false, cadence, NowClock); // sets DigestCadence only
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, NowClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // An ACTIVE watch, optionally narrowed to "endast matchade" (OnlyMatched, no ort dimension). org.nr
    // is irrelevant to the dispatch pass (watches load by UserId, map by watch.Id) but must be valid.
    private static async Task<CompanyWatchId> SeedWatchAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, bool onlyMatched,
        CancellationToken ct)
    {
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000).ToString(
            "D8", System.Globalization.CultureInfo.InvariantCulture);
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, NowClock).Value;
        if (onlyMatched)
            watch.SetFilter(WatchFilterSpec.Create([], [], onlyMatched: true).Value).IsSuccess
                .ShouldBeTrue("SetFilter ska lyckas på en aktiv watch");
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    // A Pending follow hit for (userId, adId, watchId). createdAt staggers the recency order so the
    // OrderByDescending(CreatedAt) alone decides it and the ThenBy(Id) VO tiebreaker is never invoked
    // under EF InMemory (parity the Strong-match staggering rationale above).
    private static async Task SeedFollowHitAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, JobAdId adId,
        CompanyWatchId watchId, CancellationToken ct, DateTimeOffset? createdAt = null)
    {
        var hit = FollowedCompanyAdHit.Create(userId, adId, watchId, new ClockAt(createdAt ?? NowClock.UtcNow)).Value;
        db.FollowedCompanyAdHits.Add(hit);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<FollowedCompanyAdHit?> ReloadHitAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, JobAdId adId,
        CancellationToken ct) =>
        await db.FollowedCompanyAdHits.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.UserId == userId && h.JobAdId == adId, ct);

    // An ASSESSABLE profile: non-empty Fast.SsykGroupConceptIds → FilterToMatchingAsync IS consulted.
    private static FullCandidateMatchProfile AssessableProfile() =>
        new(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []);

    // A PROFILE-LESS profile: empty Fast.SsykGroupConceptIds → the "endast matchade" filter is INERT.
    private static FullCandidateMatchProfile ProfilelessProfile() =>
        new(new CandidateMatchProfile("", [], [], [], []), []);

    // ── D3 crux + 8C: OnlyMatched + assessable → ≥Good hit drains, below-floor hit LEFT PENDING.
    [Fact]
    public async Task RunAsync_OnlyMatchedWatch_AssessableProfile_DrainsMatchingHit_LeavesFilteredOutPending()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var watchId = await SeedWatchAsync(db, userId, onlyMatched: true, ct);

        var ad1 = await SeedActiveAdAsync(db, "Roll 1", "Bolag 1", ct);
        var ad2 = await SeedActiveAdAsync(db, "Roll 2", "Bolag 2", ct);
        await SeedFollowHitAsync(db, userId, ad1, watchId, ct, createdAt: NowClock.UtcNow);
        await SeedFollowHitAsync(db, userId, ad2, watchId, ct, createdAt: NowClock.UtcNow.AddMinutes(-1));

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        // Only ad1 grades ≥Good; ad2 is below the fixed "matchande"-floor under the OnlyMatched watch.
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { ad1 });

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        (await ReloadHitAsync(db, userId, ad1, ct))!.NotificationStatus
            .ShouldBe(FollowedCompanyAdHitStatus.Sent, "≥Good-hit dräneras (Sent)");
        (await ReloadHitAsync(db, userId, ad2, ct))!.NotificationStatus
            .ShouldBe(FollowedCompanyAdHitStatus.Pending,
                "en bortfiltrerad hit (under ≥Good under en OnlyMatched-watch) LÄMNAS Pending — " +
                "aldrig claimad, aldrig dränerad (8C retroaktiv åter-ytning)");
    }

    // ── INERT: OnlyMatched watch + profile-less user → the fail-fast port is NEVER called, all pass.
    [Fact]
    public async Task RunAsync_OnlyMatchedWatch_ProfilelessUser_FilterIsInert_DispatchesUnfiltered()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var watchId = await SeedWatchAsync(db, userId, onlyMatched: true, ct);
        var ad = await SeedActiveAdAsync(db, "Roll", "Bolag", ct);
        await SeedFollowHitAsync(db, userId, ad, watchId, ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfilelessProfile()); // empty-SSYK → filter INERT

        FollowedCompanyNotificationEmail? captured = null;
        await _emailSender.SendFollowedCompanyNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<FollowedCompanyNotificationEmail>(c => captured = c),
            Arg.Any<FollowedCompanyNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        // The ≥Good SQL port fail-fasts on an empty-SSYK profile → the assessability branch must gate it
        // out entirely (never call it) for a profile-less user.
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
        // The hit is delivered unfiltered (RF-5 under-fork: deliver rather than a dishonest empty set).
        (await ReloadHitAsync(db, userId, ad, ct))!.NotificationStatus
            .ShouldBe(FollowedCompanyAdHitStatus.Sent);
        // An INERT OnlyMatched filter is NOT disclosed as active (§5 accuracy) → no summary at all.
        captured.ShouldNotBeNull();
        captured.FilterSummary.ShouldBeNull(
            "ett inert OnlyMatched-filter (profil-lös user) redovisas ALDRIG som aktivt");
    }

    // ── No OnlyMatched filter → the grade path is dormant (neither port touched); all hits dispatched.
    [Fact]
    public async Task RunAsync_NoFilterWatch_DispatchesAllHits_GradePathDormant_NoFilterSummary()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var watchId = await SeedWatchAsync(db, userId, onlyMatched: false, ct); // no filter at all

        var ad1 = await SeedActiveAdAsync(db, "Roll 1", "Bolag 1", ct);
        var ad2 = await SeedActiveAdAsync(db, "Roll 2", "Bolag 2", ct);
        await SeedFollowHitAsync(db, userId, ad1, watchId, ct, createdAt: NowClock.UtcNow);
        await SeedFollowHitAsync(db, userId, ad2, watchId, ct, createdAt: NowClock.UtcNow.AddMinutes(-1));

        FollowedCompanyNotificationEmail? captured = null;
        await _emailSender.SendFollowedCompanyNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<FollowedCompanyNotificationEmail>(c => captured = c),
            Arg.Any<FollowedCompanyNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        (await ReloadHitAsync(db, userId, ad1, ct))!.NotificationStatus
            .ShouldBe(FollowedCompanyAdHitStatus.Sent);
        (await ReloadHitAsync(db, userId, ad2, ct))!.NotificationStatus
            .ShouldBe(FollowedCompanyAdHitStatus.Sent);

        // The common path: the user has NO OnlyMatched watch → the grade path is dormant AND no profile
        // is built at all (the gate is an in-memory Any() over already-loaded data, never a per-digest
        // profile build).
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
        await _profileBuilder.DidNotReceive().BuildFullForUserIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        captured.ShouldNotBeNull();
        // REWRITTEN under CTO sub-bind A′ (2026-07-12): the old assertion message ("inget aktivt filter
        // FORMADE MEJLET") encoded the contributing-watch quantifier that A′ removed. The claim is now a
        // statement about the user's SETTINGS — this user has no active watch filter at all — and that is
        // what makes the null summary a TRUE claim rather than merely an absent one.
        captured.FilterSummary.ShouldBeNull(
            "användaren har inga aktiva bevakningsfilter → ingen disclosure (och null är här ett SANT " +
            "påstående: inget av företagen du följer är filtrerat)");
    }

    // ═══════════════════════ CTO sub-bind A′ (2026-07-12) — the quantifier is the user's SETTINGS
    //
    // BuildFilterSummary used to quantify over the watches that CONTRIBUTED a hit to this email. A watch
    // whose filter suppressed 100% of that company's new ads contributes ZERO hits, so it was invisible
    // to the summary: the email disclosed NOTHING while ads were really being suppressed — the silent
    // narrowing RF-13 rejected, reached by a second route (security-auditor; CTO ruled it blocking).
    // Worse, the rendered sentence already says "ett eller flera av FÖRETAGEN DU FÖLJER" (all of them),
    // so the code was narrower than the sentence it printed, and the disclosure's ABSENCE was a claim
    // that could be false.
    //
    // Every test below FAILS against the pre-A′ code. They share one shape: a filtered watch that
    // contributes NOTHING to this email, plus a second UNFILTERED watch that supplies the hits (the
    // digest needs at least one pending hit to exist at all).

    [Fact]
    public async Task RunAsync_OrtFilteredWatchContributingZeroHits_StillDisclosesLocationFilter()
    {
        // The ort axis is the sharp case: 8A never creates the hit row, so a 100%-suppressed watch leaves
        // NO trace in this email's hit set. An event-scoped summary cannot even in principle see it —
        // which is why the quantifier has to be the settings, not the event.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        await SeedGeoFilteredWatchAsync(db, userId, ["kommun_a"], [], ct); // suppressed 100% at scan → 0 hits
        var openWatchId = await SeedWatchAsync(db, userId, onlyMatched: false, ct); // supplies the hits
        var ad = await SeedActiveAdAsync(db, "Roll", "Bolag", ct);
        await SeedFollowHitAsync(db, userId, ad, openWatchId, ct);

        var captured = await RunAndCaptureAsync(db, ct);

        captured.ShouldNotBeNull();
        var summary = captured.FilterSummary.ShouldNotBeNull(
            "en ort-filtrerad bevakning som tystade ALLA sina annonser bidrar noll hits — och måste " +
            "ändå redovisas, annars är mejlets tystnad ett falskt påstående (sub-bind A′)");
        summary.LocationFilterActive.ShouldBeTrue();
        summary.OnlyMatchedActive.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_OnlyMatchedWatchWithAllHitsGradedOut_StillDisclosesOnlyMatched()
    {
        // The OnlyMatched watch's hits are ALL graded out → it contributes nothing to `effective`, so the
        // pre-A′ summary (built from `effective`) never saw it. The unfiltered watch keeps the email
        // alive. The filtered-out hit stays Pending (8C) — that part is unchanged.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var gradedWatchId = await SeedWatchAsync(db, userId, onlyMatched: true, ct);
        var openWatchId = await SeedWatchAsync(db, userId, onlyMatched: false, ct);

        var gradedOutAd = await SeedActiveAdAsync(db, "Bortgraderad", "Bolag 1", ct);
        var openAd = await SeedActiveAdAsync(db, "Ofiltrerad", "Bolag 2", ct);
        await SeedFollowHitAsync(db, userId, gradedOutAd, gradedWatchId, ct, createdAt: NowClock.UtcNow);
        await SeedFollowHitAsync(db, userId, openAd, openWatchId, ct, createdAt: NowClock.UtcNow.AddMinutes(-1));

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId>()); // inget graderar ≥Good → hela den watchens bidrag faller

        var captured = await RunAndCaptureAsync(db, ct);

        captured.ShouldNotBeNull();
        var summary = captured.FilterSummary.ShouldNotBeNull();
        summary.OnlyMatchedActive.ShouldBeTrue(
            "filtret tystade sina annonser fullständigt — det är MER anledning att redovisa det, inte mindre");
        (await ReloadHitAsync(db, userId, gradedOutAd, ct))!.NotificationStatus
            .ShouldBe(FollowedCompanyAdHitStatus.Pending, "8C: en bortfiltrerad hit lämnas Pending");
    }

    [Fact]
    public async Task RunAsync_AssessableUser_OnlyMatchedWatchWithNoPendingHits_StillDisclosesOnlyMatched()
    {
        // The case a naive fix gets wrong, and the reason assessability had to become a USER-level
        // property. The OnlyMatched watch has NO pending hits at all → `idsToGrade` is empty → the
        // pre-A′ code never built a profile, so `gradeAssessable` stayed false and the disclosure stayed
        // silent even though the user IS assessable and the filter IS live. Assessability is a property
        // of the user, not of this email's hit set.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        await SeedWatchAsync(db, userId, onlyMatched: true, ct); // no hits this window
        var openWatchId = await SeedWatchAsync(db, userId, onlyMatched: false, ct);
        var ad = await SeedActiveAdAsync(db, "Roll", "Bolag", ct);
        await SeedFollowHitAsync(db, userId, ad, openWatchId, ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());

        var captured = await RunAndCaptureAsync(db, ct);

        captured.ShouldNotBeNull();
        captured.FilterSummary.ShouldNotBeNull().OnlyMatchedActive.ShouldBeTrue(
            "ett aktivt OnlyMatched-filter redovisas även när den bevakningen inte hade några hits alls");

        // The profile is built AT MOST ONCE, and the fail-fast ≥Good port is not called when there is
        // nothing to grade (assessability alone must not drag the SQL port into the common flow).
        await _profileBuilder.Received(1).BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>());
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ProfilelessUser_OnlyMatchedWatchWithNoPendingHits_DisclosesNothing()
    {
        // The mirror of the test above: A′ widened WHO is asked, not WHAT counts as narrowing. A
        // profile-less user's OnlyMatched filter is INERT (it suppresses nothing), so disclosing it as
        // active would be a §5 accuracy miss in the other direction — claiming ads are missing when none
        // are. The profile is still built exactly once (that is how inertness is discovered).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        await SeedWatchAsync(db, userId, onlyMatched: true, ct); // no hits, and inert anyway
        var openWatchId = await SeedWatchAsync(db, userId, onlyMatched: false, ct);
        var ad = await SeedActiveAdAsync(db, "Roll", "Bolag", ct);
        await SeedFollowHitAsync(db, userId, ad, openWatchId, ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfilelessProfile());

        var captured = await RunAndCaptureAsync(db, ct);

        captured.ShouldNotBeNull();
        captured.FilterSummary.ShouldBeNull(
            "ett inert OnlyMatched-filter redovisas aldrig som aktivt — det narrowar ingenting");
        await _profileBuilder.Received(1).BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>());
    }

    // Runs the weekly digest and returns the follow-email contract the sender was handed (null when no
    // follow email was sent).
    private async Task<FollowedCompanyNotificationEmail?> RunAndCaptureAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, CancellationToken ct)
    {
        FollowedCompanyNotificationEmail? captured = null;
        await _emailSender.SendFollowedCompanyNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<FollowedCompanyNotificationEmail>(c => captured = c),
            Arg.Any<FollowedCompanyNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);
        return captured;
    }

    // ── 13B: an assessable OnlyMatched watch that contributed a hit discloses OnlyMatchedActive=true.
    [Fact]
    public async Task RunAsync_OnlyMatchedWatch_AssessableProfile_PopulatesFilterSummary_OnlyMatchedActive()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var watchId = await SeedWatchAsync(db, userId, onlyMatched: true, ct);
        var ad = await SeedActiveAdAsync(db, "Roll", "Bolag", ct);
        await SeedFollowHitAsync(db, userId, ad, watchId, ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(AssessableProfile());
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { ad }); // the hit passes ≥Good

        FollowedCompanyNotificationEmail? captured = null;
        await _emailSender.SendFollowedCompanyNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<FollowedCompanyNotificationEmail>(c => captured = c),
            Arg.Any<FollowedCompanyNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        captured.ShouldNotBeNull();
        var summary = captured.FilterSummary.ShouldNotBeNull(
            "en assessable OnlyMatched-watch som bidrog en hit ska redovisa filtret (13B)");
        summary.OnlyMatchedActive.ShouldBeTrue();
        summary.LocationFilterActive.ShouldBeFalse("ingen ort-dimension på denna watch");
    }

    // ── 13B geo-disclosure: an ACTIVE geo filter ALWAYS narrows (scan-time, 8A), so it always discloses.
    //
    // Both axes must disclose. The email is demonstrably shorter than it would have been — hits were
    // never created for the filtered-out ads — and silent narrowing was rejected on §5-grounds (RF-13).
    // The disclosure is driven by the per-watch WatchFilterSpec, so a spec axis the summary does not
    // know about produces a filtered email with NO disclosure at all: the exact failure RF-13 exists to
    // prevent, and invisible in every log.

    // An ACTIVE watch narrowed on the GEO dimension (kommun and/or län; OnlyMatched stays false, so the
    // grade path is dormant and the geo axis is isolated).
    private static async Task<CompanyWatchId> SeedGeoFilteredWatchAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId,
        IReadOnlyList<string> municipalities, IReadOnlyList<string> regions, CancellationToken ct)
    {
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000).ToString(
            "D8", System.Globalization.CultureInfo.InvariantCulture);
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, NowClock).Value;
        watch.SetFilter(WatchFilterSpec.Create(municipalities, regions, onlyMatched: false).Value)
            .IsSuccess.ShouldBeTrue("SetFilter ska lyckas på en aktiv watch");
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    // Seeds one ad + one pending hit on `watchId`, then runs the weekly digest and returns the captured
    // follow-email contract.
    private async Task<FollowedCompanyNotificationEmail?> RunAndCaptureFollowEmailAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId, CompanyWatchId watchId,
        CancellationToken ct)
    {
        var ad = await SeedActiveAdAsync(db, "Roll", "Bolag", ct);
        await SeedFollowHitAsync(db, userId, ad, watchId, ct);
        return await RunAndCaptureAsync(db, ct);
    }

    [Fact]
    public async Task RunAsync_MunicipalityFilteredWatch_PopulatesFilterSummary_LocationFilterActive()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var watchId = await SeedGeoFilteredWatchAsync(db, userId, ["kommun_a"], [], ct);

        var captured = await RunAndCaptureFollowEmailAsync(db, userId, watchId, ct);

        captured.ShouldNotBeNull();
        var summary = captured.FilterSummary.ShouldNotBeNull(
            "en kommun-filtrerad watch som bidrog en hit ska redovisa ortsfiltret (13B)");
        summary.LocationFilterActive.ShouldBeTrue();
        summary.OnlyMatchedActive.ShouldBeFalse("ingen grade-dimension på denna watch");
    }

    [Fact]
    public async Task RunAsync_RegionOnlyFilteredWatch_PopulatesFilterSummary_LocationFilterActive()
    {
        // F4a REGRESSION PIN. A whole-län watch carries its selection on the REGION axis (a län pick is
        // never expanded into kommun-ids — that expansion is the silent-miss bug F4a removes). Its
        // Municipalities list is therefore EMPTY, and a disclosure rule that only looks at
        // Municipalities.Count reports "no location filter" for a mail that WAS narrowed by location:
        // the user is silently told nothing about the ads the scan suppressed. Location is disclosed
        // when EITHER geo axis is active.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var watchId = await SeedGeoFilteredWatchAsync(db, userId, [], ["lan_skane"], ct);

        var captured = await RunAndCaptureFollowEmailAsync(db, userId, watchId, ct);

        captured.ShouldNotBeNull();
        var summary = captured.FilterSummary.ShouldNotBeNull(
            "en län-filtrerad watch narrowar mejlet lika hårt som en kommun-filtrerad — tyst " +
            "smalning är exakt det RF-13=13B avvisade");
        summary.LocationFilterActive.ShouldBeTrue(
            "län-axeln är en ORT-dimension: LocationFilterActive måste vara sann när NÅGON geo-axel " +
            "är aktiv, annars saknar ett filtrerat mejl sin disclosure");
    }

    // A one-off clock for stamping a match's CreatedAt at a chosen instant.
    // ═══════════════════════════════════════════════════════════════════════════════════════
    // #842 — THE TOMBSTONE MUST NOT BE EMAILED.
    //
    // An erased ad is a ROW, not a hole: JobAd.Erase() blanks it and leaves it in the table, so
    // an unguarded join projects Title = "" and Company = "[raderad]" — the tombstone's own marker
    // — straight into an outbound email. These two joins are the ONLY places in the product where
    // that marker LEAVES THE SYSTEM BOUNDARY, and until these tests existed you could delete
    // either `where j.Status != JobAdStatus.Erased` line and the whole suite stayed green.
    //
    // EF InMemory is the right level HERE and it is not a shortcut: what these tests pin is
    // BEHAVIOUR (the guard exists, the email omits the ad, the row is still drained). The SQL
    // TRANSLATION of the identical expression is pinned separately, on real Postgres, by
    // ErasedAdReadPathTests — because InMemory honours record equality in LINQ-to-objects and
    // would pass whether or not Npgsql can translate `!= Erased`.
    // ═══════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RunAsync_ErasedAd_IsNOT_emailed_in_the_match_digest_and_the_row_is_still_drained()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, DigestCadence.Weekly, ct);

        var erasedAd = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong,
            "Raderad roll", "Raderat bolag", ct, createdAt: NowClock.UtcNow);
        var liveAd = await SeedMatchAsync(db, userId, NotifiableMatchGrade.Strong,
            "Kvar roll", "Kvar bolag", ct, createdAt: NowClock.UtcNow.AddMinutes(-1));

        // Through the PRODUCTION transition, never by writing columns (#843).
        var ad = await db.JobAds.FirstAsync(j => j.Id == erasedAd, ct);
        ad.Erase(NowClock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        MatchNotificationEmail? captured = null;
        await _emailSender.SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<MatchNotificationEmail>(c => captured = c),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        captured.ShouldNotBeNull();
        captured.Items.Select(i => i.CompanyName).ShouldNotContain(Company.Erased.Name,
            "the tombstone's marker '[raderad]' would be EMAILED to the user. This is one of only "
            + "two places in the product where an erased ad leaves the system boundary.");
        captured.Items.Select(i => i.JobTitle).ShouldNotContain(string.Empty);
        captured.Items.Select(i => i.CompanyName).ShouldContain("Kvar bolag");

        // And the erased match is still DRAINED: it WAS a valid match when detected, and leaving it
        // Pending would retry it on every digest, forever.
        (await ReloadMatchAsync(db, userId, erasedAd, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent,
                "drain-but-do-not-show. Not showing it is not the same as not resolving it.");
        (await ReloadMatchAsync(db, userId, liveAd, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task RunAsync_ErasedAd_IsNOT_emailed_in_the_followed_company_digest_and_the_hit_is_drained()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedFollowConsentingSeekerAsync(db, DigestCadence.Weekly, ct);
        var watchId = await SeedWatchAsync(db, userId, onlyMatched: false, ct);

        var erasedAd = await SeedActiveAdAsync(db, "Raderad roll", "Raderat bolag", ct);
        var liveAd = await SeedActiveAdAsync(db, "Kvar roll", "Kvar bolag", ct);
        await SeedFollowHitAsync(db, userId, erasedAd, watchId, ct, createdAt: NowClock.UtcNow);
        await SeedFollowHitAsync(db, userId, liveAd, watchId, ct, createdAt: NowClock.UtcNow.AddMinutes(-1));

        var ad = await db.JobAds.FirstAsync(j => j.Id == erasedAd, ct);
        ad.Erase(NowClock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        FollowedCompanyNotificationEmail? captured = null;
        await _emailSender.SendFollowedCompanyNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<FollowedCompanyNotificationEmail>(c => captured = c),
            Arg.Any<FollowedCompanyNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        captured.ShouldNotBeNull();
        captured.Items.Select(i => i.CompanyName).ShouldNotContain(Company.Erased.Name,
            "the second — and last — place the tombstone can leave the system: the followed-company "
            + "digest email.");
        captured.Items.Select(i => i.CompanyName).ShouldContain("Kvar bolag");

        (await ReloadHitAsync(db, userId, erasedAd, ct))!.NotificationStatus
            .ShouldBe(FollowedCompanyAdHitStatus.Sent, "drained, not shown.");
    }

    private sealed class ClockAt(DateTimeOffset utcNow) : Jobbliggaren.Domain.Common.IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
