using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Application.UnitTests.Common;
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

    private const string ToEmail = "seeker@example.com";

    public DigestDispatchJobTests()
    {
        _userAccounts.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ToEmail);
    }

    private DigestDispatchJob CreateJob(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, int maxItems = 20) =>
        new(db, _emailSender, _userAccounts, NowClock,
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
            Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        await _emailSender.Received(1).SendMatchNotificationEmailAsync(
            ToEmail, Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
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
            Arg.Any<CancellationToken>());

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
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
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
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
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
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
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
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
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
            Arg.Any<CancellationToken>());

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
                Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>())
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
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());

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
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
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
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());

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
            ToEmail, Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
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
            Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(DigestCadence.Weekly, ct);

        captured.ShouldNotBeNull();
        captured.Items.Select(i => i.JobTitle).ShouldBe(["Nyast", "Mitten", "Äldst"]);
    }

    // A one-off clock for stamping a match's CreatedAt at a chosen instant.
    private sealed class ClockAt(DateTimeOffset utcNow) : Jobbliggaren.Domain.Common.IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
