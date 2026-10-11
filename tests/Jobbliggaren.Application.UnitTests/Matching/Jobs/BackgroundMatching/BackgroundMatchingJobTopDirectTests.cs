using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
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
/// ADR 0080 Vag 4 PR-4b, ADR 0161 (#2106) — UNIT cover for the scan and the send phase TOGETHER: the
/// scan persists, then <see cref="TopMatchMailDispatcher"/> mails in the same run. The scan-loop
/// invariants live in <see cref="BackgroundMatchingJobTests"/>, the send phase's own in
/// <see cref="TopMatchMailDispatcherTests"/>; THIS class pins what only the two together show:
/// <list type="bullet">
/// <item>a new <see cref="NotifiableMatchGrade.Top"/> match sends ONE Direct email (one item, the
///   ad's Title/Company + "Toppmatch", TotalCount=1) and the row ends
///   <see cref="NotificationStatus.Sent"/>; two Top matches of one run go in ONE email;</item>
/// <item><see cref="NotifiableMatchGrade.Strong"/> is NEVER emailed here (digest) — the row persists
///   Pending;</item>
/// <item>no account email, or a send failure: the rows are claimed and stay Queued, the matches
///   persist, RunAsync does not throw, and no later run sends them;</item>
/// <item>every user's scan commits before the first send, and the send phase runs once per run;</item>
/// <item>an ad re-read inside the settle window is not matched twice, so it is mailed once;</item>
/// <item>a fresh Top match for an ad older than the direct-mail window is persisted, not mailed.</item>
/// </list>
/// The collaborators are NSubstitute mocks; the <see cref="IMatchScorer"/> returns a hand-built
/// <see cref="FullMatchScore"/> (no real engine), and the IAppDbContext is the real-AppDbContext-over
/// -EF-InMemory fake — the SAME seam <see cref="BackgroundMatchingJobTests"/> uses. The grade is
/// produced by the real <c>MatchGradeCalculator.Grade(FullMatchScore)</c> from the seeded score, so
/// the dispatch routing (Top→email, Strong→none) is exercised through the production grade path, not
/// asserted on a label we injected.
/// </summary>
public class BackgroundMatchingJobTopDirectTests
{
    private static readonly FakeDateTimeProvider NowClock =
        new(new DateTimeOffset(2026, 6, 24, 3, 20, 0, TimeSpan.Zero));

    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IMatchScorer _scorer = Substitute.For<IMatchScorer>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly IUserAccountService _userAccounts = Substitute.For<IUserAccountService>();

    private const string ToEmail = "seeker@example.com";

    public BackgroundMatchingJobTopDirectTests()
    {
        _userAccounts.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ToEmail);
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

    // A run's clock that moves between reads, as a real one does, so the rows one run writes carry
    // distinct CreatedAt. The send phase orders by CreatedAt and then by id, and EF InMemory cannot
    // compare the strongly-typed id on a tie. The run reads its own "now" first, so the watermark is
    // still start minus IngestSettleWindow.
    private static AdvancingFakeDateTimeProvider RunClock(DateTimeOffset start) =>
        new(start, TimeSpan.FromMilliseconds(1));

    // ───────────────────────────── Score recipes (against MatchGradeCalculator.Grade(FullMatchScore))

    private static MatchDimension Match() => new(MatchDimensionVerdict.Match, [], []);
    private static MatchDimension NotAssessed() => new(MatchDimensionVerdict.NotAssessed, [], []);

    // Top: Ssyk/Region/Employment Match + must-have Match (gate met) + a skill signal (Match) →
    // both secondaries confirmed + signal → Top. Title/nice NotAssessed (irrelevant to Top).
    private static FullMatchScore TopScore() => new(
        Fast: new MatchScore(Match(), NotAssessed(), Match(), Match()),
        SkillOverlap: Match(),
        MustHaveCoverage: Match(),
        NiceToHaveCoverage: NotAssessed());

    // Strong: same gate (Ssyk/Region/Employment + must-have Match) but NO skill/nice signal (both
    // NotAssessed) → both secondaries confirmed, gate met, no signal → Strong (digest, not direct).
    private static FullMatchScore StrongScore() => new(
        Fast: new MatchScore(Match(), NotAssessed(), Match(), Match()),
        SkillOverlap: NotAssessed(),
        MustHaveCoverage: Match(),
        NiceToHaveCoverage: NotAssessed());

    // Related (PR-4 #300): a would-be Strong tuple (Ssyk/Region/Employment Match + must-have Match)
    // BUT carried on a related-only SSYK hit. Paired with SsykIsRelated:true on the carrier, the
    // Worker's Grade(FullMatchScore, isRelated) caps flat to MatchGrade.Related, which ToNotifiable
    // maps to null → the match is scored but NEVER persisted (ADR 0084 fråga D — Related is not
    // notifiable, list-only). The Fast tuple is deliberately Strong-shaped so the ONLY reason it is
    // dropped is the related cap, not a weak tuple.
    private static FullMatchScore WouldBeStrongScore() => StrongScore();

    // ───────────────────────────── Seeding

    // A consenting JobSeeker (opt-in ON, never withdrawn) → inside the consenting set.
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

    // An Active JobAd ingested at `now` (or at `ingestedAt`) → CreatedAt > since (since = now - 7d
    // for a never-scanned user) → inside the cold-start window the scan reads. Title + Company.Name
    // feed the email item.
    private static async Task<JobAdId> SeedActiveAdAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        string title, string company, CancellationToken ct, DateTimeOffset? ingestedAt = null)
    {
        var ingestClock = ingestedAt is { } at ? new FakeDateTimeProvider(at) : NowClock;
        var jobAd = JobAd.Create(
            title, Company.Create(company).Value, "Beskrivning", "https://example.com/jobb",
            JobSource.Platsbanken, ingestClock.UtcNow, ingestClock.UtcNow.AddDays(30), ingestClock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // A FULL profile with a STATED occupation (non-empty SSYK) → passes the SSYK gate so the scan
    // reaches scoring. The actual dimension verdicts come from the mocked scorer, not this profile.
    private static FullCandidateMatchProfile ProfileWithOccupation() =>
        new(new CandidateMatchProfile(
                Title: "Systemutvecklare",
                SsykGroupConceptIds: ["2512"],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            CvSkillConceptIds: []);

    // PR-4 (#300, ADR 0084): ScoreFullBatchAsync returns FullScoredMatch carriers. The scan
    // unwraps .Score to grade and maps Good/Strong/Top → notifiable (Related → null, not
    // persisted). These Top-direct tests are behaviour-inert on the related bit (SsykIsRelated:
    // false), so the dispatch routing is unchanged — only the carrier wrapping is new.
    private void StubScorer(
        JobAdId jobAdId, FullMatchScore score, IReadOnlyList<string>? matchedSkills = null) =>
        _scorer.ScoreFullBatchAsync(
                Arg.Any<IReadOnlyList<JobAdId>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<JobAdId, FullScoredMatch>
            {
                [jobAdId] = new FullScoredMatch(score, SsykIsRelated: false, matchedSkills ?? [], MatchDimensionCauses.None, new FullMatchConceptEvidence(new([], []), new([], []), new([], []))),
            });

    private void StubScorer(IReadOnlyDictionary<JobAdId, FullMatchScore> scores) =>
        _scorer.ScoreFullBatchAsync(
                Arg.Any<IReadOnlyList<JobAdId>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<CancellationToken>())
            .Returns(scores.ToDictionary(
                pair => pair.Key,
                pair => new FullScoredMatch(pair.Value, SsykIsRelated: false, [], MatchDimensionCauses.None, new FullMatchConceptEvidence(new([], []), new([], []), new([], [])))));

    // PR-4 (#300): stub the carrier with SsykIsRelated:true → the Worker grades Related (flat cap)
    // → ToNotifiable(Related) == null → the match is scored but NOT persisted.
    private void StubScorerRelated(JobAdId jobAdId, FullMatchScore score) =>
        _scorer.ScoreFullBatchAsync(
                Arg.Any<IReadOnlyList<JobAdId>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<JobAdId, FullScoredMatch>
            {
                [jobAdId] = new FullScoredMatch(score, SsykIsRelated: true, [], MatchDimensionCauses.None, new FullMatchConceptEvidence(new([], []), new([], []), new([], []))),
            });

    private static async Task<UserJobAdMatch?> ReloadMatchAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId, JobAdId jobAdId, CancellationToken ct) =>
        await db.UserJobAdMatches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.JobAdId == jobAdId, ct);

    // ───────────────────────────── 1. Top → one Direct email, row Sent

    [Fact]
    public async Task RunAsync_TopMatch_SendsOneDirectEmail_AndMarksRowSent()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Backend-utvecklare", "Acme AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(jobAdId, TopScore());

        MatchNotificationEmail? captured = null;
        string? capturedTo = null;
        await _emailSender.SendMatchNotificationEmailAsync(
            Arg.Do<string>(to => capturedTo = to),
            Arg.Do<MatchNotificationEmail>(c => captured = c),
            Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(ct);

        // Exactly one Direct email to the resolved address.
        await _emailSender.Received(1).SendMatchNotificationEmailAsync(
            ToEmail, Arg.Any<MatchNotificationEmail>(),
            Arg.Any<CancellationToken>());
        capturedTo.ShouldBe(ToEmail);
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(MatchNotificationKind.Direct);
        captured.Cadence.ShouldBeNull("Direct har ingen kadens");
        captured.TotalCount.ShouldBe(1);
        captured.Items.Count.ShouldBe(1, "en fokuserad Direct-mejl per Top-match");
        captured.Items[0].JobTitle.ShouldBe("Backend-utvecklare");
        captured.Items[0].CompanyName.ShouldBe("Acme AB");
        captured.Items[0].GradeLabel.ShouldBe("Toppmatch");
        captured.Items[0].JobAdId.ShouldBe(jobAdId.Value, "the direct mail links /jobb/{id} of this ad (#2094)");

        // The claim-then-send spine ends with the row Sent.
        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldNotBeNull("Top-matchen ska persisteras");
        match.Grade.ShouldBe(NotifiableMatchGrade.Top);
        match.NotificationStatus.ShouldBe(NotificationStatus.Sent);
    }

    // ───────────────────────────── 2. Strong → NO email, row Pending

    [Fact]
    public async Task RunAsync_StrongMatch_DoesNotEmail_AndRowStaysPending()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Frontend-utvecklare", "Globex AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(jobAdId, StrongScore());

        await CreateJob(db).RunAsync(ct);

        // Strong is digest-only — the scan never directly emails it.
        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<CancellationToken>());

        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldNotBeNull("Strong-matchen ska persisteras (notifierbar grad)");
        match.Grade.ShouldBe(NotifiableMatchGrade.Strong);
        match.NotificationStatus.ShouldBe(NotificationStatus.Pending,
            "Strong ska ligga kvar Pending för digesten — aldrig direkt-mejlad");
    }

    // ───────────────────────────── 2a. #477 Low 2 — SkillOverlap evidence is PERSISTED

    // Before #477 Low 2 the scan wrote UserJobAdMatch.Create(..., [], ...) — the persisted
    // explainability evidence never existed. Now it persists the covered-skill concept-ids the
    // scorer surfaces on the carrier. The LastMatchScanAt + UNIQUE(UserId,JobAdId) spine prevent
    // re-derivation, so this is the ONLY place the evidence can be captured.
    [Fact]
    public async Task RunAsync_PersistsMatchedSkillConceptIds_FromTheScorerCarrier()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Backend-utvecklare", "Acme AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        var covered = new[] { "skill-csharp", "skill-sql" };
        StubScorer(jobAdId, StrongScore(), covered);

        await CreateJob(db).RunAsync(ct);

        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldNotBeNull();
        match.MatchedSkillConceptIds.ShouldBe(covered, ignoreOrder: true,
            "the scan persists the scorer's covered-skill evidence, not an empty list (#477 Low 2)");
    }

    // A pathological >Max intersection must NOT fail Create (which would silently drop the match) —
    // the scan truncates to UserJobAdMatch.MaxMatchedSkills. Regression guard: before the truncate,
    // Create's >Max validation would have made this notifiable match vanish.
    [Fact]
    public async Task RunAsync_TruncatesMatchedSkillEvidence_ToTheAggregateCap_MatchStillPersists()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Backend-utvecklare", "Acme AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        var many = Enumerable.Range(0, UserJobAdMatch.MaxMatchedSkills + 10)
            .Select(i => $"skill-{i:D3}").ToArray();
        StubScorer(jobAdId, StrongScore(), many);

        await CreateJob(db).RunAsync(ct);

        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldNotBeNull(
            "the match must still persist despite >Max evidence — never silently dropped");
        // Pins the count AND which ids: the deterministic first-Max prefix (Take over the scorer's
        // already-Ordinal-ordered set), not an arbitrary Max subset.
        match.MatchedSkillConceptIds.ShouldBe(many.Take(UserJobAdMatch.MaxMatchedSkills));
    }

    // The common real case: a notifiable match (SSYK/region/employment-driven) whose SkillOverlap
    // is empty (Vacuous/NotAssessed → empty carrier) persists with EMPTY evidence and is NOT
    // dropped — Create tolerates an empty list.
    [Fact]
    public async Task RunAsync_PersistsEmptyEvidence_WhenNoSkillsCovered_MatchNotDropped()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Backend-utvecklare", "Acme AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(jobAdId, StrongScore()); // default covered = [] (no skill overlap)

        await CreateJob(db).RunAsync(ct);

        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldNotBeNull("a notifiable match with no covered skills must still persist");
        match.MatchedSkillConceptIds.ShouldBeEmpty();
    }

    // ───────────────────────────── 2b. Related → scored but NOT persisted (list-only, no notis)

    // PR-4 (#300, ADR 0084 fråga D): a related-only match is SCORED but NOT persisted as a
    // UserJobAdMatch — ToNotifiable(Related) == null → it never drives notifications (no email, no
    // row). The Fast tuple is Strong-shaped, so the ONLY reason it is dropped is the related cap.
    [Fact]
    public async Task RunAsync_RelatedMatch_IsScoredButNotPersisted_AndDoesNotEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Närliggande-utvecklare", "Hooli AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        // Related carrier (SsykIsRelated:true) over a would-be-Strong tuple → Worker grades Related.
        StubScorerRelated(jobAdId, WouldBeStrongScore());

        await CreateJob(db).RunAsync(ct);

        // The scorer WAS invoked (the ad was scored), but Related is not notifiable → no row.
        await _scorer.Received(1).ScoreFullBatchAsync(
            Arg.Any<IReadOnlyList<JobAdId>>(),
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<CancellationToken>());

        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldBeNull(
            "en related-only-match ska scoras men ALDRIG persisteras (ToNotifiable(Related) == null, " +
            "ADR 0084 fråga D — Related är list-only, driver ingen notis).");

        // No email either (no notifiable row to dispatch).
        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<CancellationToken>());
    }

    // ───────────────────────────── 3. No account email → no send, Top left Queued

    // The send phase claims before it reads the address (ADR 0161 D5(c)), so an address-less account's
    // row is claimed and left Queued for the reaper, never sent by a later run.
    [Fact]
    public async Task RunAsync_TopMatchButNoAccountEmail_DoesNotEmail_AndRowIsLeftQueued()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Dataingenjör", "Initech AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(jobAdId, TopScore());
        // Orphan consent row without an account email.
        _userAccounts.GetEmailAsync(userId, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // The scan must not throw despite the missing recipient.
        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<CancellationToken>());

        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldNotBeNull("matchen ska persisteras även utan mottagaradress");
        match.NotificationStatus.ShouldBe(NotificationStatus.Queued,
            "raden claim:as före adressläsningen och ligger kvar Queued, aldrig skickad");
    }

    // ───────────────────────────── 4. Send throws → row left Queued, match persists, no throw

    [Fact]
    public async Task RunAsync_SendThrows_LeavesRowQueued_MatchPersists_AndDoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Testautomatiserare", "Umbrella AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(jobAdId, TopScore());
        _emailSender.SendMatchNotificationEmailAsync(
                Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mejlleverans nere"));

        // A send failure is caught and logged, never propagated out of the run.
        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldNotBeNull("matchen persisteras före (och oberoende av) sändningen");
        match.NotificationStatus.ShouldBe(NotificationStatus.Queued,
            "en claim:ad rad vars sändning faller ligger kvar Queued (aldrig dubbel-mejlad; TD-114)");
        match.SentAt.ShouldBeNull("en misslyckad sändning ska inte stämpla SentAt");
    }

    // ───────────────────────────── 5. Two Top matches → one Direct email listing both

    [Fact]
    public async Task RunAsync_TwoTopMatches_SendsOneDirectEmailListingBoth()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var adA = await SeedActiveAdAsync(db, "Utvecklare A", "Företag A AB", ct);
        var adB = await SeedActiveAdAsync(db, "Utvecklare B", "Företag B AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        _scorer.ScoreFullBatchAsync(
                Arg.Any<IReadOnlyList<JobAdId>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<JobAdId, FullScoredMatch>
            {
                [adA] = new FullScoredMatch(TopScore(), SsykIsRelated: false, [], MatchDimensionCauses.None, new FullMatchConceptEvidence(new([], []), new([], []), new([], []))),
                [adB] = new FullScoredMatch(TopScore(), SsykIsRelated: false, [], MatchDimensionCauses.None, new FullMatchConceptEvidence(new([], []), new([], []), new([], []))),
            });

        MatchNotificationEmail? captured = null;
        await _emailSender.SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Do<MatchNotificationEmail>(c => captured = c), Arg.Any<CancellationToken>());

        await CreateJob(db, RunClock(NowClock.UtcNow)).RunAsync(ct);

        // One recipient, one mail per run, bundling every due Top match (ADR 0161 D5).
        await _emailSender.Received(1).SendMatchNotificationEmailAsync(
            ToEmail, Arg.Any<MatchNotificationEmail>(),
            Arg.Any<CancellationToken>());
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(MatchNotificationKind.Direct);
        captured.Items.Select(item => item.JobAdId).ShouldBe([adA.Value, adB.Value], ignoreOrder: true);
        captured.TotalCount.ShouldBe(2);

        (await ReloadMatchAsync(db, userId, adA, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent);
        (await ReloadMatchAsync(db, userId, adB, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent);
    }

    // ───────────────────────────── 6. Two Top matches, the one send throws → both Queued, never re-sent

    // The two rows travel in one mail, so one failed send leaves both claimed. The next run does not
    // send them again ("never twice" over "never miss"; the reaper fails them later).
    [Fact]
    public async Task RunAsync_TwoTopMatches_SendThrows_LeavesBothRowsQueued_AndTheNextRunDoesNotResend()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var adA = await SeedActiveAdAsync(db, "Utvecklare A", "Företag A AB", ct);
        var adB = await SeedActiveAdAsync(db, "Utvecklare B", "Företag B AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(new Dictionary<JobAdId, FullMatchScore> { [adA] = TopScore(), [adB] = TopScore() });
        _emailSender.SendMatchNotificationEmailAsync(
                Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mejlleverans nere"));

        await Should.NotThrowAsync(() => CreateJob(db, RunClock(NowClock.UtcNow)).RunAsync(ct));
        await CreateJob(db, RunClock(NowClock.UtcNow.AddMinutes(5))).RunAsync(ct);

        await _emailSender.Received(1).SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
        (await ReloadMatchAsync(db, userId, adA, ct))!.NotificationStatus.ShouldBe(NotificationStatus.Queued);
        (await ReloadMatchAsync(db, userId, adB, ct))!.NotificationStatus.ShouldBe(NotificationStatus.Queued);
    }

    // ───────────────────────────── 7. The scan persists, then the send phase runs once

    // Two users with a Top match each. When the first mail goes out, BOTH users' scans have committed:
    // the scan never sends, and the send phase starts after the whole scan. The send phase opens one
    // scope per run, the only one that resolves the account service.
    [Fact]
    public async Task RunAsync_EveryScanCommitsBeforeTheFirstSend_AndTheSendPhaseRunsOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userA = await SeedConsentingSeekerAsync(db, ct);
        var userB = await SeedConsentingSeekerAsync(db, ct);
        var ad = await SeedActiveAdAsync(db, "Backend-utvecklare", "Acme AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(ad, TopScore());

        var watermarksAtEachSend = new List<DateTimeOffset?[]>();
        _emailSender
            .When(sender => sender.SendMatchNotificationEmailAsync(
                Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>()))
            .Do(_ => watermarksAtEachSend.Add(
            [
                .. db.JobSeekers.AsNoTracking()
                    .Where(js => js.UserId == userA || js.UserId == userB)
                    .Select(js => js.LastMatchScanAt)
                    .ToList(),
            ]));

        var (job, scopes) = Compose(db, RunClock(NowClock.UtcNow));
        await job.RunAsync(ct);

        watermarksAtEachSend.Count.ShouldBe(2);
        var scannedThrough = NowClock.UtcNow - BackgroundMatchingJob.IngestSettleWindow;
        foreach (var watermarks in watermarksAtEachSend)
            watermarks.ShouldAllBe(watermark => watermark == scannedThrough,
                "every user's scan committed before any mail went out");
        scopes.Resolutions<IUserAccountService>().ShouldBe(1, "the send phase runs once per run");
    }

    // ───────────────────────────── 8. Never twice across runs

    // Run 1 matches an ad ingested inside the settle window; run 2 re-reads it (the watermark trails
    // the clock by IngestSettleWindow) and re-scores it, and the existing-pair check keeps it from a
    // second row and a second mail. EF InMemory enforces no unique index, so a second row would show.
    [Fact]
    public async Task RunAsync_AdReReadInsideTheSettleWindow_IsNotMatchedTwice_AndIsMailedOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var clock = new MutableClock(NowClock.UtcNow);
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var ad = await SeedActiveAdAsync(db, "Backend-utvecklare", "Acme AB", ct,
            NowClock.UtcNow.AddMinutes(-1));

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(ad, TopScore());

        await CreateJob(db, clock).RunAsync(ct);
        clock.Advance(TimeSpan.FromMinutes(1));
        await CreateJob(db, clock).RunAsync(ct);

        await _scorer.Received(2).ScoreFullBatchAsync(
            Arg.Is<IReadOnlyList<JobAdId>>(ids => ids.Contains(ad)),
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<CancellationToken>());
        (await db.UserJobAdMatches.CountAsync(m => m.UserId == userId && m.JobAdId == ad, ct)).ShouldBe(1);
        await _emailSender.Received(1).SendMatchNotificationEmailAsync(
            ToEmail, Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────── 9. An old ad's fresh Top match is not mailed

    // security-auditor V2: a never-scanned user's first scan reaches ColdStartDays back and writes a
    // fresh Top row for an ad ingested three days ago. The match persists; it is not mailed as news.
    [Fact]
    public async Task RunAsync_TopMatchForAnAdOlderThanTheDirectMailWindow_IsPersistedButNotMailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var oldAd = await SeedActiveAdAsync(db, "Backend-utvecklare", "Acme AB", ct,
            NowClock.UtcNow.AddDays(-3));

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(oldAd, TopScore());

        await CreateJob(db).RunAsync(ct);

        var match = await ReloadMatchAsync(db, userId, oldAd, ct);
        match.ShouldNotBeNull();
        match.Grade.ShouldBe(NotifiableMatchGrade.Top);
        match.NotificationStatus.ShouldBe(NotificationStatus.Pending);
        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(), Arg.Any<CancellationToken>());
    }
}
