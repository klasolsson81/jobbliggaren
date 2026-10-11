using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Common.Notifications;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Application.Matching.Jobs.StrandedMatchReaper;
using Jobbliggaren.Application.UnitTests.Admin.HostObservations;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Jobs.BackgroundMatching;

/// <summary>
/// ADR 0161 (#2106) — the send phase of the background-matching run. The rows are what the scan
/// writes: <c>UserJobAdMatch.Create</c> at the scan's clock, for an ad ingested before it
/// (<c>BackgroundMatchingJob.ScanUserAsync</c>). Later states come from their own actors: an earlier
/// run's claim and send (<c>MarkQueued</c>/<c>MarkSent</c>), <c>JobAd.Archive</c>, and the user's
/// <c>JobSeeker.UpdateNotificationConsent</c> and <c>AccountDeletionScheduler</c>'s
/// <c>JobSeeker.SoftDelete</c>. The context is a real AppDbContext over EF InMemory.
/// </summary>
public class TopMatchMailDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 2, 0, TimeSpan.Zero);
    private static readonly FakeDateTimeProvider NowClock = new(Now);
    private static readonly TimeSpan Window = TopMatchMailDispatcher.DirectMailWindow;

    private readonly Jobbliggaren.Infrastructure.Persistence.AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUserAccountService _userAccounts = Substitute.For<IUserAccountService>();

    public TopMatchMailDispatcherTests()
    {
        _userAccounts.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => EmailOf(call.Arg<Guid>()));
    }

    private static string EmailOf(Guid userId) => $"u{userId:N}@example.com";

    // Every send attempt, in order, a failed one included.
    private List<(string To, MatchNotificationEmail Mail)> Sends =>
    [
        .. _emailSender.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IEmailSender.SendMatchNotificationEmailAsync))
            .Select(call => (call.GetArguments()[0] as string ?? "", (MatchNotificationEmail)call.GetArguments()[1]!)),
    ];

    private TopMatchMailDispatcher Dispatcher(IDateTimeProvider? clock = null) =>
        new(new FakeMatchingScopeFactory(
                _db, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IMatchScorer>(), _userAccounts),
            _emailSender, clock ?? NowClock, NullLogger<TopMatchMailDispatcher>.Instance);

    private Task<int> DispatchAsync(IDateTimeProvider? clock = null) =>
        Dispatcher(clock).DispatchAsync(MailPacingRule.TopMatch, TestContext.Current.CancellationToken);

    private async Task<JobSeeker> SeedConsentingSeekerAsync()
    {
        var since = new FakeDateTimeProvider(Now.AddDays(-30));
        var seeker = JobSeeker.Register(Guid.NewGuid(), TermsAcceptance.AcceptCurrent(since), since).Value;
        seeker.UpdateNotificationConsent(enabled: true, since);
        _db.JobSeekers.Add(seeker);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return seeker;
    }

    private async Task<JobAd> SeedAdAsync(DateTimeOffset ingestedAt, string title = "Systemutvecklare")
    {
        var ingest = new FakeDateTimeProvider(ingestedAt);
        var ad = JobAd.Create(
            title, Company.Create("Acme AB").Value, "Beskrivning", "https://example.com/jobb",
            JobSource.Platsbanken, ingestedAt, ingestedAt.AddDays(30), ingest).Value;
        _db.JobAds.Add(ad);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return ad;
    }

    // Due rows get distinct CreatedAt: the send phase orders by CreatedAt and then by id, and EF InMemory
    // cannot compare the strongly-typed id on a tie.
    private async Task<UserJobAdMatch> SeedMatchAsync(
        Guid userId, JobAd ad, DateTimeOffset matchedAt, NotifiableMatchGrade grade = NotifiableMatchGrade.Top)
    {
        var match = UserJobAdMatch.Create(userId, ad.Id, grade, [], new FakeDateTimeProvider(matchedAt)).Value;
        _db.UserJobAdMatches.Add(match);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return match;
    }

    // A new ad and its fresh Top match, as one scan run leaves them.
    private async Task<UserJobAdMatch> SeedFreshTopMatchAsync(Guid userId, DateTimeOffset matchedAt)
    {
        var ad = await SeedAdAsync(matchedAt.AddMinutes(-3));
        return await SeedMatchAsync(userId, ad, matchedAt);
    }

    private async Task<UserJobAdMatch> ReloadAsync(UserJobAdMatch match) =>
        await _db.UserJobAdMatches.AsNoTracking()
            .SingleAsync(row => row.Id == match.Id, TestContext.Current.CancellationToken);

    private async Task<NotificationStatus> StatusOfAsync(UserJobAdMatch match) =>
        (await ReloadAsync(match)).NotificationStatus;

    // ───────────────────────────── The due set

    [Fact]
    public async Task DispatchAsync_NothingDue_SendsNothingAndWritesNothing()
    {
        await SeedConsentingSeekerAsync();
        var saves = 0;
        _db.SavingChanges += (_, _) => saves++;

        var mailed = await DispatchAsync();

        mailed.ShouldBe(0);
        Sends.ShouldBeEmpty();
        saves.ShouldBe(0);
    }

    [Fact]
    public async Task DispatchAsync_OnlyTopRowsAreDue_StrongAndGoodStayPending()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var top = await SeedMatchAsync(seeker.UserId, await SeedAdAsync(Now.AddHours(-1), "Topp"), Now.AddMinutes(-5));
        var strong = await SeedMatchAsync(
            seeker.UserId, await SeedAdAsync(Now.AddHours(-1), "Stark"), Now.AddMinutes(-5), NotifiableMatchGrade.Strong);
        var good = await SeedMatchAsync(
            seeker.UserId, await SeedAdAsync(Now.AddHours(-1), "Bra"), Now.AddMinutes(-5), NotifiableMatchGrade.Good);

        await DispatchAsync();

        Sends.Count.ShouldBe(1);
        Sends[0].Mail.Items.Select(item => item.JobAdId).ShouldBe([top.JobAdId.Value]);
        (await StatusOfAsync(top)).ShouldBe(NotificationStatus.Sent);
        (await StatusOfAsync(strong)).ShouldBe(NotificationStatus.Pending, "Strong is the digest's");
        (await StatusOfAsync(good)).ShouldBe(NotificationStatus.Pending, "Good is never mailed");
    }

    // An earlier run claimed one row and its send failed (Queued), and mailed the other (Sent).
    [Fact]
    public async Task DispatchAsync_OnlyPendingRowsAreDue_QueuedAndSentRowsAreNeverSentAgain()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var queued = await SeedFreshTopMatchAsync(seeker.UserId, Now.AddMinutes(-10));
        var sent = await SeedFreshTopMatchAsync(seeker.UserId, Now.AddMinutes(-10));
        queued.MarkQueued();
        sent.MarkQueued();
        sent.MarkSent(new FakeDateTimeProvider(Now.AddMinutes(-9)));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var mailed = await DispatchAsync();

        mailed.ShouldBe(0);
        Sends.ShouldBeEmpty();
        (await StatusOfAsync(queued)).ShouldBe(NotificationStatus.Queued);
        (await ReloadAsync(sent)).SentAt.ShouldBe(Now.AddMinutes(-9));
    }

    [Fact]
    public async Task DispatchAsync_RowForAnAdArchivedSinceTheMatch_IsNotMailed()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var ad = await SeedAdAsync(Now.AddHours(-1));
        var match = await SeedMatchAsync(seeker.UserId, ad, Now.AddMinutes(-5));
        ad.Archive(new FakeDateTimeProvider(Now.AddMinutes(-1)));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await DispatchAsync();

        Sends.ShouldBeEmpty();
        (await StatusOfAsync(match)).ShouldBe(NotificationStatus.Pending);
    }

    // security-auditor V2: the clock starts at the ad's ingest. The scan reaches ColdStartDays back on a
    // first scan or a re-consent, so it writes a fresh row for an ad days old; that row is not news.
    [Fact]
    public async Task DispatchAsync_FreshRowForAnAdOlderThanTheWindow_IsNotMailed()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var oldAd = await SeedAdAsync(Now.AddDays(-3));
        var match = await SeedMatchAsync(seeker.UserId, oldAd, Now.AddMinutes(-1));

        await DispatchAsync();

        Sends.ShouldBeEmpty();
        (await StatusOfAsync(match)).ShouldBe(NotificationStatus.Pending);
    }

    [Fact]
    public async Task DispatchAsync_AdIngestedExactlyTheWindowAgo_IsStillMailed()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var match = await SeedMatchAsync(seeker.UserId, await SeedAdAsync(Now - Window), Now.AddMinutes(-1));

        await DispatchAsync();

        Sends.Count.ShouldBe(1);
        (await StatusOfAsync(match)).ShouldBe(NotificationStatus.Sent);
    }

    // The scan wrote the rows, the user withdrew before any run claimed them, and turned consent back
    // on more than a window later. Rows and ads are both older than the window by then.
    [Fact]
    public async Task DispatchAsync_RowsOlderThanTheWindowAfterAReConsent_AreNeverMailed()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var matchedAt = Now - Window - TimeSpan.FromHours(6);
        var match = await SeedFreshTopMatchAsync(seeker.UserId, matchedAt);
        seeker.UpdateNotificationConsent(enabled: false, new FakeDateTimeProvider(matchedAt.AddSeconds(30)));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        seeker.UpdateNotificationConsent(enabled: true, NowClock);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await DispatchAsync();

        Sends.ShouldBeEmpty();
        (await StatusOfAsync(match)).ShouldBe(NotificationStatus.Pending,
            "Pending honestly means never notified; the match stays visible in /matchningar");
    }

    // UNREACHABLE, declared per AGENTS.md §5 Tests: a row is created by the scan after its ad committed,
    // and JobAd.CreatedAt is set only in the constructor, so a row is never older than its ad and no
    // path produces a row older than the window for an ad inside it. This asserts only that the send
    // phase degrades safely if that ever breaks: the stale row is not mailed.
    [Fact]
    public async Task DispatchAsync_IfARowEverPredatedItsAd_TheStaleRowIsNotMailed()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var ad = await SeedAdAsync(Now.AddHours(-1));
        var match = await SeedMatchAsync(seeker.UserId, ad, Now - Window - TimeSpan.FromMinutes(1));

        await DispatchAsync();

        Sends.ShouldBeEmpty();
        (await StatusOfAsync(match)).ShouldBe(NotificationStatus.Pending);
    }

    // The scan wrote the row while the user consented; the withdrawal landed before the send phase.
    // The row is never claimed, so it stays Pending rather than Queued.
    [Fact]
    public async Task DispatchAsync_RowsOfAUserWhoWithdrew_AreNeverClaimed_WhileOthersAreMailed()
    {
        var withdrawn = await SeedConsentingSeekerAsync();
        var consenting = await SeedConsentingSeekerAsync();
        var withdrawnRow = await SeedFreshTopMatchAsync(withdrawn.UserId, Now.AddMinutes(-10));
        var consentingRow = await SeedFreshTopMatchAsync(consenting.UserId, Now.AddMinutes(-5));
        withdrawn.UpdateNotificationConsent(enabled: false, new FakeDateTimeProvider(Now.AddMinutes(-1)));
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await DispatchAsync();

        Sends.Select(send => send.To).ShouldBe([EmailOf(consenting.UserId)]);
        (await StatusOfAsync(withdrawnRow)).ShouldBe(NotificationStatus.Pending);
        (await StatusOfAsync(consentingRow)).ShouldBe(NotificationStatus.Sent);
    }

    // ───────────────────────────── One mail per recipient

    [Fact]
    public async Task DispatchAsync_SeveralDueRowsOfOneRecipient_GoInOneMail_AndAreAllMarkedSent()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var rows = new List<UserJobAdMatch>();
        string[] titles = ["Utvecklare A", "Utvecklare B", "Utvecklare C"];
        for (var i = 0; i < titles.Length; i++)
            rows.Add(await SeedMatchAsync(seeker.UserId, await SeedAdAsync(Now.AddHours(-1), titles[i]), Now.AddMinutes(-5 + i)));

        var mailed = await DispatchAsync();

        mailed.ShouldBe(1);
        Sends.Count.ShouldBe(1);
        var (to, mail) = Sends[0];
        to.ShouldBe(EmailOf(seeker.UserId));
        mail.Kind.ShouldBe(MatchNotificationKind.Direct);
        mail.Cadence.ShouldBeNull();
        mail.Items.Count.ShouldBe(3);
        mail.TotalCount.ShouldBe(mail.Items.Count, "the mail lists exactly the rows it claimed (#864)");
        mail.Items.Select(item => item.JobAdId).ShouldBe(rows.Select(row => row.JobAdId.Value));
        mail.Items.Select(item => item.JobTitle).ShouldBe(titles);
        mail.Items.ShouldAllBe(item => item.CompanyName == "Acme AB" && item.GradeLabel == "Toppmatch");
        foreach (var row in rows)
        {
            var reloaded = await ReloadAsync(row);
            reloaded.NotificationStatus.ShouldBe(NotificationStatus.Sent);
            reloaded.SentAt.ShouldBe(Now);
        }
    }

    [Fact]
    public async Task DispatchAsync_MoreDueRowsThanMaxItemsPerMail_ListsTheCap_AndTheNextRunSendsTheRest()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var rows = new List<UserJobAdMatch>();
        for (var i = 0; i <= TopMatchMailDispatcher.MaxItemsPerMail; i++)
            rows.Add(await SeedMatchAsync(
                seeker.UserId, await SeedAdAsync(Now.AddHours(-1), $"Utvecklare {i}"), Now.AddMinutes(-30).AddSeconds(i)));
        var newest = rows[^1];

        await DispatchAsync();

        Sends.Count.ShouldBe(1);
        Sends[0].Mail.Items.Select(item => item.JobAdId)
            .ShouldBe(rows.Take(TopMatchMailDispatcher.MaxItemsPerMail).Select(row => row.JobAdId.Value),
                "the oldest rows are listed first");
        Sends[0].Mail.TotalCount.ShouldBe(TopMatchMailDispatcher.MaxItemsPerMail);
        foreach (var row in rows.SkipLast(1))
            (await StatusOfAsync(row)).ShouldBe(NotificationStatus.Sent);
        (await StatusOfAsync(newest)).ShouldBe(NotificationStatus.Pending);

        await DispatchAsync();

        Sends.Count.ShouldBe(2);
        Sends[1].Mail.Items.Select(item => item.JobAdId).ShouldBe([newest.JobAdId.Value]);
        Sends[1].Mail.TotalCount.ShouldBe(1);
        foreach (var row in rows)
            (await StatusOfAsync(row)).ShouldBe(NotificationStatus.Sent);

        (await DispatchAsync()).ShouldBe(0);

        Sends.Count.ShouldBe(2);
    }

    // ───────────────────────────── The run cap and the order

    // One more recipient than the Top rule's cap. Seeded newest first, so an unordered read would mail
    // the newest and defer the oldest.
    [Fact]
    public async Task DispatchAsync_MoreRecipientsThanTheRunCap_MailsTheOldestFirst_AndTheNextRunSendsTheRest()
    {
        var recipients = MailPacingRule.TopMatch.MaxMailsPerRun + 1;
        var byAge = new List<(JobSeeker Seeker, UserJobAdMatch Row)>();
        for (var minutesAgo = 1; minutesAgo <= recipients; minutesAgo++)
        {
            var seeker = await SeedConsentingSeekerAsync();
            byAge.Add((seeker, await SeedFreshTopMatchAsync(seeker.UserId, Now.AddMinutes(-minutesAgo))));
        }
        var newest = byAge[0];

        var mailed = await DispatchAsync();

        mailed.ShouldBe(MailPacingRule.TopMatch.MaxMailsPerRun);
        Sends.Select(send => send.To).ShouldNotContain(EmailOf(newest.Seeker.UserId));
        (await StatusOfAsync(newest.Row)).ShouldBe(NotificationStatus.Pending);
        foreach (var (_, row) in byAge.Skip(1))
            (await StatusOfAsync(row)).ShouldBe(NotificationStatus.Sent);

        var mailedNext = await DispatchAsync();

        mailedNext.ShouldBe(1);
        Sends[^1].To.ShouldBe(EmailOf(newest.Seeker.UserId));
        (await StatusOfAsync(newest.Row)).ShouldBe(NotificationStatus.Sent);
    }

    // ───────────────────────────── Failures

    [Fact]
    public async Task DispatchAsync_FirstSendFails_StopsThePhase_AndThatRecipientsRowsAreNeverSentAgain()
    {
        var failing = await SeedConsentingSeekerAsync();
        var waiting = await SeedConsentingSeekerAsync();
        var failingRow = await SeedFreshTopMatchAsync(failing.UserId, Now.AddMinutes(-10));
        var waitingRow = await SeedFreshTopMatchAsync(waiting.UserId, Now.AddMinutes(-5));
        _emailSender.SendMatchNotificationEmailAsync(
                EmailOf(failing.UserId), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EmailDeliveryException("match-notification", "HttpRequestException"));

        await Should.NotThrowAsync(() => DispatchAsync());

        Sends.Select(send => send.To).ShouldBe([EmailOf(failing.UserId)]);
        (await StatusOfAsync(failingRow)).ShouldBe(NotificationStatus.Queued);
        (await ReloadAsync(failingRow)).SentAt.ShouldBeNull();
        (await StatusOfAsync(waitingRow)).ShouldBe(NotificationStatus.Pending, "the phase stopped before it");

        await DispatchAsync();

        Sends.Select(send => send.To).ShouldBe([EmailOf(failing.UserId), EmailOf(waiting.UserId)]);
        (await StatusOfAsync(failingRow)).ShouldBe(NotificationStatus.Queued, "a claimed row is never re-sent");
        (await StatusOfAsync(waitingRow)).ShouldBe(NotificationStatus.Sent);
    }

    // UNREACHABLE, declared per AGENTS.md §5 Tests: a consenting, live JobSeeker always has an account
    // address. A present account without one breaks the #822 invariant (IUserAccountService), and no
    // path in src/ removes the account of a live JobSeeker: the hard delete takes soft-deleted seekers
    // only, and AccountHardDeleter only detects a reverse orphan (#508). This asserts only that the send
    // phase degrades safely if that ever breaks: nothing is sent to that recipient, its rows stay
    // Queued, the next recipient is still mailed, and no later run sends them.
    [Fact]
    public async Task DispatchAsync_IfAConsentingRecipientEverHadNoAddress_NothingIsSentToIt_AndTheNextIsMailed()
    {
        var noAddress = await SeedConsentingSeekerAsync();
        var next = await SeedConsentingSeekerAsync();
        var noAddressRow = await SeedFreshTopMatchAsync(noAddress.UserId, Now.AddMinutes(-10));
        var nextRow = await SeedFreshTopMatchAsync(next.UserId, Now.AddMinutes(-5));
        _userAccounts.GetEmailAsync(noAddress.UserId, Arg.Any<CancellationToken>()).Returns((string?)null);

        var mailed = await DispatchAsync();

        mailed.ShouldBe(1);
        Sends.Select(send => send.To).ShouldBe([EmailOf(next.UserId)]);
        (await StatusOfAsync(noAddressRow)).ShouldBe(NotificationStatus.Queued);
        (await StatusOfAsync(nextRow)).ShouldBe(NotificationStatus.Sent);

        await DispatchAsync();

        Sends.Count.ShouldBe(1);
    }

    // The user's withdrawal commits between the claim's commit and the send (GDPR Art. 7(3)).
    [Fact]
    public async Task DispatchAsync_ConsentWithdrawnAfterTheClaim_SendsNothing_AndLeavesTheRowsQueued()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var row = await SeedFreshTopMatchAsync(seeker.UserId, Now.AddMinutes(-5));
        CommitAfterTheClaim(() => seeker.UpdateNotificationConsent(enabled: false, NowClock));

        var mailed = await DispatchAsync();

        mailed.ShouldBe(0);
        Sends.ShouldBeEmpty();
        (await StatusOfAsync(row)).ShouldBe(NotificationStatus.Queued);
    }

    // The account's deletion (AccountDeletionScheduler soft-deletes the JobSeeker) commits between the
    // claim's commit and the send (GDPR Art. 17).
    [Fact]
    public async Task DispatchAsync_AccountDeletedAfterTheClaim_SendsNothing_AndLeavesTheRowsQueued()
    {
        var seeker = await SeedConsentingSeekerAsync();
        var row = await SeedFreshTopMatchAsync(seeker.UserId, Now.AddMinutes(-5));
        CommitAfterTheClaim(() => seeker.SoftDelete(NowClock));

        var mailed = await DispatchAsync();

        mailed.ShouldBe(0);
        Sends.ShouldBeEmpty();
        (await StatusOfAsync(row)).ShouldBe(NotificationStatus.Queued);
    }

    // Runs the user's own write once, committed right after the dispatcher's first commit (its claim).
    private void CommitAfterTheClaim(Action usersWrite)
    {
        var done = false;
        _db.SavedChanges += (_, _) =>
        {
            if (done)
                return;
            done = true;
            usersWrite();
            _db.SaveChanges();
        };
    }

    // A slow provider: the first recipient's send outlasts SendBudget, so no further recipient starts.
    [Fact]
    public async Task DispatchAsync_SendBudgetSpent_StartsNoFurtherRecipient_AndLeavesTheirRowsPending()
    {
        var clock = new MutableClock(Now);
        var first = await SeedConsentingSeekerAsync();
        var second = await SeedConsentingSeekerAsync();
        var firstRow = await SeedFreshTopMatchAsync(first.UserId, Now.AddMinutes(-10));
        var secondRow = await SeedFreshTopMatchAsync(second.UserId, Now.AddMinutes(-5));
        _emailSender
            .When(sender => sender.SendMatchNotificationEmailAsync(
                EmailOf(first.UserId), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>()))
            .Do(_ => clock.Advance(TopMatchMailDispatcher.SendBudget));

        var mailed = await DispatchAsync(clock);

        mailed.ShouldBe(1);
        (await StatusOfAsync(firstRow)).ShouldBe(NotificationStatus.Sent);
        (await StatusOfAsync(secondRow)).ShouldBe(NotificationStatus.Pending);
        Sends.Select(send => send.To).ShouldNotContain(EmailOf(second.UserId));
    }

    // ───────────────────────────── The window against the reaper

    // StrandedThresholdHours is private, so it is read by reflection: the due set (Pending, younger than
    // the window) and the reaper's set (Queued, older than its threshold) stay apart only while the
    // window is the shorter.
    [Fact]
    public void DirectMailWindow_IsShorterThanTheStrandedReapersThreshold()
    {
        var field = typeof(StrandedMatchReaperJob).GetField(
            "StrandedThresholdHours", BindingFlags.NonPublic | BindingFlags.Static);
        field.ShouldNotBeNull("StrandedMatchReaperJob.StrandedThresholdHours was renamed; re-point this pin");
        var reaperThreshold = TimeSpan.FromHours((int)field.GetRawConstantValue()!);

        TopMatchMailDispatcher.DirectMailWindow.ShouldBeLessThan(reaperThreshold);
    }
}
