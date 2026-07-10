using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Application.UnitTests.Common;
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
/// ADR 0080 Vag 4 PR-4b — UNIT cover for the Top-DIRECT email hook the scan runs AFTER the atomic
/// commit. The scan-loop invariants (consent filter, isolation, watermark) live in
/// <see cref="BackgroundMatchingJobTests"/>; THIS class pins only the dispatch contract:
/// <list type="bullet">
/// <item>a new <see cref="NotifiableMatchGrade.Top"/> match sends EXACTLY ONE focused Direct email
///   (Kind=Direct, one item carrying the ad's Title/Company + "Toppmatch", TotalCount=1) and the row
///   ends <see cref="NotificationStatus.Sent"/>;</item>
/// <item><see cref="NotifiableMatchGrade.Strong"/> is NEVER emailed here (it accumulates into the
///   digest) — the row persists Pending;</item>
/// <item>no account email → no send, the Top row stays Pending, the scan still completes;</item>
/// <item>a send failure leaves THAT row Queued (claimed but not Sent), the match still persists, and
///   RunAsync does not throw (per-match isolation);</item>
/// <item>two Top matches → two separate Direct emails (one per match).</item>
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

    private BackgroundMatchingJob CreateJob(Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(db, _profileBuilder, _scorer, _emailSender, _userAccounts, NowClock,
            NullLogger<BackgroundMatchingJob>.Instance);

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
        var seeker = JobSeeker.Register(userId, "Test", NowClock).Value;
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, NowClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return userId;
    }

    // An Active JobAd created at `now` → CreatedAt > since (since = now - 7d for a never-scanned
    // user) → inside the cold-start window the scan queries. Title + Company.Name feed the email item.
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
                [jobAdId] = new FullScoredMatch(score, SsykIsRelated: false, matchedSkills ?? []),
            });

    // PR-4 (#300): stub the carrier with SsykIsRelated:true → the Worker grades Related (flat cap)
    // → ToNotifiable(Related) == null → the match is scored but NOT persisted.
    private void StubScorerRelated(JobAdId jobAdId, FullMatchScore score) =>
        _scorer.ScoreFullBatchAsync(
                Arg.Any<IReadOnlyList<JobAdId>>(),
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<JobAdId, FullScoredMatch>
            {
                [jobAdId] = new FullScoredMatch(score, SsykIsRelated: true, []),
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
            Arg.Any<MatchNotificationIdempotencyKey>(),
            Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(ct);

        // Exactly one Direct email to the resolved address.
        await _emailSender.Received(1).SendMatchNotificationEmailAsync(
            ToEmail, Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
        capturedTo.ShouldBe(ToEmail);
        captured.ShouldNotBeNull();
        captured.Kind.ShouldBe(MatchNotificationKind.Direct);
        captured.Cadence.ShouldBeNull("Direct har ingen kadens");
        captured.TotalCount.ShouldBe(1);
        captured.Items.Count.ShouldBe(1, "en fokuserad Direct-mejl per Top-match");
        captured.Items[0].JobTitle.ShouldBe("Backend-utvecklare");
        captured.Items[0].CompanyName.ShouldBe("Acme AB");
        captured.Items[0].GradeLabel.ShouldBe("Toppmatch");

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
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

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
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    // ───────────────────────────── 3. No account email → no send, Top stays Pending

    [Fact]
    public async Task RunAsync_TopMatchButNoAccountEmail_DoesNotEmail_AndRowStaysPending()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Dataingenjör", "Initech AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(jobAdId, TopScore());
        // Orphan consent row without an account email → the dispatch skips (rows stay Pending).
        _userAccounts.GetEmailAsync(userId, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // The scan must not throw despite the missing recipient.
        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        await _emailSender.DidNotReceiveWithAnyArgs().SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldNotBeNull("matchen ska persisteras även utan mottagaradress");
        match.NotificationStatus.ShouldBe(NotificationStatus.Pending,
            "utan mottagaradress claim:as raden inte — den ligger kvar Pending");
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
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mejlleverans nere"));

        // Per-match isolation: a send failure is caught + logged, never propagated.
        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        var match = await ReloadMatchAsync(db, userId, jobAdId, ct);
        match.ShouldNotBeNull("matchen persisteras före (och oberoende av) sändningen");
        match.NotificationStatus.ShouldBe(NotificationStatus.Queued,
            "en claim:ad rad vars sändning faller ligger kvar Queued (aldrig dubbel-mejlad; TD-114)");
        match.SentAt.ShouldBeNull("en misslyckad sändning ska inte stämpla SentAt");
    }

    // ───────────────────────────── 5. Two Top matches → two Direct emails

    [Fact]
    public async Task RunAsync_TwoTopMatches_SendsTwoDirectEmails_OnePerMatch()
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
                [adA] = new FullScoredMatch(TopScore(), SsykIsRelated: false, []),
                [adB] = new FullScoredMatch(TopScore(), SsykIsRelated: false, []),
            });

        await CreateJob(db).RunAsync(ct);

        // One focused Direct email PER Top match (not one batched email for both).
        await _emailSender.Received(2).SendMatchNotificationEmailAsync(
            ToEmail, Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        (await ReloadMatchAsync(db, userId, adA, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent);
        (await ReloadMatchAsync(db, userId, adB, ct))!.NotificationStatus
            .ShouldBe(NotificationStatus.Sent);
    }

    // ───────────────────────────── 6. Two Top matches, first send throws → second STILL attempted

    // Per-match isolation WITHIN a user: the dispatch loop's per-iteration try/catch must keep
    // attempting the remaining Top matches after one send fails (the "never miss" half of the
    // trade-off). A regression hoisting the try/catch outside the loop would swallow every Top
    // after the first failure yet still pass tests 4 and 5.
    [Fact]
    public async Task RunAsync_TwoTopMatches_FirstSendThrows_SecondStillAttempted()
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
                [adA] = new FullScoredMatch(TopScore(), SsykIsRelated: false, []),
                [adB] = new FullScoredMatch(TopScore(), SsykIsRelated: false, []),
            });

        // First send throws, the second succeeds (dispatch order over the score dict is not
        // asserted — only that BOTH were attempted and exactly one of each outcome resulted).
        _emailSender.SendMatchNotificationEmailAsync(
                Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new InvalidOperationException("första sändningen faller"),
                _ => Task.CompletedTask);

        await Should.NotThrowAsync(() => CreateJob(db).RunAsync(ct));

        // BOTH Top matches were attempted — the first failure did not abort the second.
        await _emailSender.Received(2).SendMatchNotificationEmailAsync(
            ToEmail, Arg.Any<MatchNotificationEmail>(),
            Arg.Any<MatchNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());

        var statuses = new[]
        {
            (await ReloadMatchAsync(db, userId, adA, ct))!.NotificationStatus,
            (await ReloadMatchAsync(db, userId, adB, ct))!.NotificationStatus,
        };
        statuses.ShouldContain(NotificationStatus.Sent, "den lyckade sändningen markeras Sent");
        statuses.ShouldContain(NotificationStatus.Queued,
            "den misslyckade ligger kvar Queued (per-match-isolering, aldrig dubbel-mejlad)");
    }

    // ───────────────────────────── 7. Top-direct carries the ad-scoped idempotency key (#187)

    // The Direct send must carry the deterministic, PII-free idempotency key derived from
    // (userId, jobAdId) so a transport retry of the SAME Top email dedupes at Resend. Pinned here at
    // the call site; the VO's own determinism lives in MatchNotificationIdempotencyKeyTests.
    [Fact]
    public async Task RunAsync_TopMatch_CarriesAdScopedIdempotencyKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var userId = await SeedConsentingSeekerAsync(db, ct);
        var jobAdId = await SeedActiveAdAsync(db, "Backend-utvecklare", "Acme AB", ct);

        _profileBuilder.BuildFullForUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        StubScorer(jobAdId, TopScore());

        MatchNotificationIdempotencyKey? capturedKey = null;
        await _emailSender.SendMatchNotificationEmailAsync(
            Arg.Any<string>(), Arg.Any<MatchNotificationEmail>(),
            Arg.Do<MatchNotificationIdempotencyKey>(k => capturedKey = k),
            Arg.Any<CancellationToken>());

        await CreateJob(db).RunAsync(ct);

        var key = capturedKey.ShouldNotBeNull();
        key.ShouldBe(MatchNotificationIdempotencyKey.ForDirect(userId, jobAdId.Value));
    }
}
