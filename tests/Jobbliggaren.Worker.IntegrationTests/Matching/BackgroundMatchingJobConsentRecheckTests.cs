using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Matching;

/// <summary>
/// ADR 0146 — <see cref="BackgroundMatchingJob"/> decides the consent on the row each attempt
/// loaded, never on the run-start due set alone, against REAL Postgres. A withdrawal committed
/// after the due-set query must stop the scan for that user: when it lands before the user's load
/// the re-check stops it, and when it lands between the load and the commit the xmin token fails the
/// commit and the one retry re-checks the reloaded row. Either way no match is stored, the
/// watermark stays where it was, and no Top email goes out.
/// <para>
/// The concurrent writers are the aggregate's own methods, committed in a context of their own and
/// run from the profile-builder seam: that call sits between the scan's load of the row and its
/// commit. Every score is Top, so a scan that got through sends an email — as the tests where a
/// user stays consenting show.
/// </para>
/// </summary>
[Collection("Worker")]
public class BackgroundMatchingJobConsentRecheckTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private static readonly DateTimeOffset Now = new(2026, 6, 1, 3, 20, 0, TimeSpan.Zero);

    private static readonly FullCandidateMatchProfile TopProfile = new(
        new CandidateMatchProfile("Top", ["ssyk-recheck"], [], [], []), []);
    private static readonly FullCandidateMatchProfile EmptySsykProfile = new(
        new CandidateMatchProfile("", [], [], [], []), []);

    [Fact]
    public async Task RunAsync_WhenTheConsentIsWithdrawnBetweenTheLoadAndTheCommit_NothingIsStoredOrSent()
    {
        var ct = TestContext.Current.CancellationToken;
        var adId = await SeedAdAsync(ct);
        var userId = await SeedConsentingSeekerAsync(ct);

        var builds = 0;
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        profileBuilder.BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (ci.Arg<Guid>() != userId)
                    return EmptySsykProfile;
                if (++builds == 1)
                    WithdrawConsent(userId);
                return TopProfile;
            });
        var emailSender = Substitute.For<IEmailSender>();

        await RunJobAsync(profileBuilder, ScorerScoringTop(adId), emailSender, AddressesFor(userId), ct);

        builds.ShouldBe(1);
        (await CountMatchesAsync(userId, ct)).ShouldBe(0);
        var stored = await ReadSeekerAsync(userId, ct);
        stored.LastMatchScanAt.ShouldBeNull();
        stored.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        await emailSender.DidNotReceive().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
    }

    // The retry's other half: a write that is not a withdrawal (here the user reading their matches
    // list mid-scan) still fails the first commit, and the retry stores what the scan found.
    [Fact]
    public async Task RunAsync_WhenAnotherWriteLandsBetweenTheLoadAndTheCommit_TheRetryStoresTheMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var adId = await SeedAdAsync(ct);
        var userId = await SeedConsentingSeekerAsync(ct);

        var builds = 0;
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        profileBuilder.BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (ci.Arg<Guid>() != userId)
                    return EmptySsykProfile;
                if (++builds == 1)
                    MarkMatchesSeen(userId);
                return TopProfile;
            });
        var emailSender = Substitute.For<IEmailSender>();

        await RunJobAsync(profileBuilder, ScorerScoringTop(adId), emailSender, AddressesFor(userId), ct);

        builds.ShouldBe(2);
        (await CountMatchesAsync(userId, ct)).ShouldBe(1);
        var stored = await ReadSeekerAsync(userId, ct);
        stored.LastMatchScanAt.ShouldBe(Now);
        stored.LastSeenMatchesAt.ShouldBe(Now);
        await emailSender.Received(1).SendMatchNotificationEmailAsync(
            AddressOf(userId), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenAnotherUserWithdrawsWhileTheFirstIsScanned_ThatUserGetsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var adId = await SeedAdAsync(ct);
        var ourA = await SeedConsentingSeekerAsync(ct);
        var ourB = await SeedConsentingSeekerAsync(ct);

        var scannedFirst = Guid.Empty;
        var withdrawn = Guid.Empty;
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        profileBuilder.BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var uid = ci.Arg<Guid>();
                if (uid != ourA && uid != ourB)
                    return EmptySsykProfile;
                if (scannedFirst == Guid.Empty)
                {
                    scannedFirst = uid;
                    withdrawn = uid == ourA ? ourB : ourA;
                    WithdrawConsent(withdrawn);
                }
                return TopProfile;
            });
        var emailSender = Substitute.For<IEmailSender>();

        await RunJobAsync(profileBuilder, ScorerScoringTop(adId), emailSender, AddressesFor(ourA, ourB), ct);

        scannedFirst.ShouldNotBe(Guid.Empty);
        (await CountMatchesAsync(scannedFirst, ct)).ShouldBe(1);
        (await ReadSeekerAsync(scannedFirst, ct)).LastMatchScanAt.ShouldBe(Now);
        await emailSender.Received(1).SendMatchNotificationEmailAsync(
            AddressOf(scannedFirst), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());

        (await CountMatchesAsync(withdrawn, ct)).ShouldBe(0);
        (await ReadSeekerAsync(withdrawn, ct)).LastMatchScanAt.ShouldBeNull();
        await emailSender.DidNotReceive().SendMatchNotificationEmailAsync(
            AddressOf(withdrawn), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
    }

    // ─────────────────────────── SUT ───────────────────────────

    private async Task RunJobAsync(
        IMatchProfileBuilder profileBuilder,
        IMatchScorer scorer,
        IEmailSender emailSender,
        IUserAccountService userAccounts,
        CancellationToken ct)
    {
        var scopeFactory = new OverridingScopeFactory(
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new Dictionary<Type, object>
            {
                [typeof(IMatchProfileBuilder)] = profileBuilder,
                [typeof(IMatchScorer)] = scorer,
                [typeof(IUserAccountService)] = userAccounts,
            });
        var job = new BackgroundMatchingJob(
            scopeFactory,
            emailSender,
            new FixedClock(Now),
            _fixture.Services.GetRequiredService<ILoggerFactory>().CreateLogger<BackgroundMatchingJob>());

        await job.RunAsync(ct);
    }

    private static IMatchScorer ScorerScoringTop(JobAdId adId)
    {
        var scorer = Substitute.For<IMatchScorer>();
        scorer.ScoreFullBatchAsync(
                Arg.Any<IReadOnlyList<JobAdId>>(),
                Arg.Is<FullCandidateMatchProfile>(p => ReferenceEquals(p, TopProfile)),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<JobAdId, FullScoredMatch>
            {
                [adId] = new(TopScore(), SsykIsRelated: false, [], MatchDimensionCauses.None),
            });
        return scorer;
    }

    private static string AddressOf(Guid userId) => $"recheck-{userId:N}@example.com";

    private static IUserAccountService AddressesFor(params Guid[] userIds)
    {
        var accounts = Substitute.For<IUserAccountService>();
        foreach (var userId in userIds)
            accounts.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns(AddressOf(userId));
        return accounts;
    }

    private static MatchDimension Match() => new(MatchDimensionVerdict.Match, [], []);
    private static MatchDimension NotAssessed() => new(MatchDimensionVerdict.NotAssessed, [], []);

    // Top: the Strong gate plus a skill signal (parity BackgroundMatchingJobTopDirectTests.TopScore).
    private static FullMatchScore TopScore() => new(
        Fast: new MatchScore(Match(), NotAssessed(), Match(), Match()),
        SkillOverlap: Match(),
        MustHaveCoverage: Match(),
        NiceToHaveCoverage: NotAssessed());

    // ─────────────────────────── The concurrent writers ───────────────────────────

    // Synchronous because the seam that runs them is: the substitute's callback returns the profile.
    private void WithdrawConsent(Guid userId) =>
        WriteSeeker(userId, seeker => seeker.UpdateNotificationConsent(enabled: false, new FixedClock(Now)));

    private void MarkMatchesSeen(Guid userId) =>
        WriteSeeker(userId, seeker => seeker.SetLastSeenMatches(Now, new FixedClock(Now)));

    private void WriteSeeker(Guid userId, Action<JobSeeker> write)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        write(db.JobSeekers.Single(js => js.UserId == userId));
        db.SaveChanges();
    }

    // ─────────────────────────── Seeding + read-back ───────────────────────────

    private async Task<JobAdId> SeedAdAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = $"recheck-{Guid.NewGuid():N}";
        var rawPayload = $"{{\"id\":\"{externalId}\"}}";
        var jobAd = JobAd.Import(
            title: "Omkontroll-annons",
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(60),
            clock: new FixedClock(Now), declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private async Task<Guid> SeedConsentingSeekerAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Now);
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        seeker.UpdateNotificationConsent(enabled: true, clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    private async Task<int> CountMatchesAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches.AsNoTracking().CountAsync(m => m.UserId == userId, ct);
    }

    private async Task<JobSeeker> ReadSeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
