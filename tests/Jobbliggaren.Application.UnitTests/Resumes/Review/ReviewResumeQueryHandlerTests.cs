using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Common.Security;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-4 (#653, ADR 0093 §D8/§D2(e)) — the read handler that runs the deterministic CV review
/// for the OWNING job seeker's CANONICAL Resume and merges the persisted finding-status overlay.
/// Mirrors <c>ReviewParsedResumeQueryHandlerTests</c>: owner-resolve, FirstOrDefault by Id +
/// JobSeekerId, cross-user attempt logged, null on not-found; the <see cref="ICvReviewEngine"/> is
/// NSubstitute-mocked (the handler under test is the orchestration + the status-overlay honesty
/// rules, NOT the engine internals — those are CvReviewEngineTests). The EF-Ignore'd
/// ResumeVersion.Content is hydrated on materialization by <see cref="FakeContentHydrationInterceptor"/>
/// (the handler linearizes it before the mocked engine; the real decrypt path stays proven by the
/// Api integration tests).
///
/// CA2012: stubbing the ValueTask-returning ICvReviewEngine.ReviewAsync is the known NSubstitute
/// analyzer false positive (parity ReviewParsedResumeQueryHandlerTests).
/// </summary>
#pragma warning disable CA2012
public class ReviewResumeQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvReviewEngine _engine = Substitute.For<ICvReviewEngine>();
    // Real rubric provider (committed asset) so the criterionId→Name heading lookup resolves against
    // the golden source (A7 → "Anti-klyschor" etc.).
    private readonly IRubricProvider _rubricProvider = CvReviewFixtures.RealRubricProvider();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public ReviewResumeQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private ReviewResumeQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _engine, _rubricProvider, _failedAccess, TestFindingFingerprinter.Instance);

    // The canonical Master content is EF-Ignore'd; the handler dereferences it before the mocked
    // engine (linearize → FromCanonical), so hydrate it on materialization like production does.
    private static Infrastructure.Persistence.AppDbContext CreateDb() =>
        TestAppDbContextFactory.Create(
            new FakeContentHydrationInterceptor(resumeContent: ResumeContent.Empty("Anna Andersson")));

    private void StubEngine(CvReviewResult result) =>
        _engine.ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(result));

    // A stubbed A7 Fail finding; the fingerprint the handler recomputes for it is deterministic, so a
    // separately-constructed identical verdict produces the SAME digest (used to build matching rows).
    private static CvCriterionVerdict A7Fail() =>
        CvCriterionVerdict.Assessed("A7", RubricCategory.Content, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(0, 6, "driven"), "klyscha")]);

    private static CvCriterionVerdict B3Pass() =>
        CvCriterionVerdict.Assessed("B3", RubricCategory.Structure, CriterionVerdict.Pass,
            [new StructuralEvidence("kontaktsektion komplett")]);

    // E3 "Typografisk konsekvens" is one of the versioned rubric's StyleOnly criteria
    // (Fas 4b PR-8.4, CTO-bind Q1) — a Warn drives the Ignorera-gate assertion below.
    private static CvCriterionVerdict E3StyleWarn() =>
        CvCriterionVerdict.Assessed("E3", RubricCategory.VisualQuality, CriterionVerdict.Warn,
            [new StructuralEvidence("ojämn typografi")]);

    // Rubric version 1.0.0 — the finding-status rows are keyed to this string so the overlay picks
    // them up (a row keyed to another version never carries over: D2(e) key boundary).
    private static readonly RubricVersion StubVersion = RubricVersion.Parse("1.0.0");

    private static CvReviewResult ResultWith(params CvCriterionVerdict[] verdicts) =>
        new(StubVersion, RenderProfile.Ats, [], verdicts, [], verdicts.Length, verdicts.Length);

    private static async Task<Resume> SeedOwnedResumeAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId, Action<Resume>? configure = null)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var resume = Resume.Create(seeker.Id, "Mitt CV", "Anna Andersson", FakeDateTimeProvider.Default).Value;
        configure?.Invoke(resume);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return resume;
    }

    // ===============================================================
    // Happy path + context/profile passthrough
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnDto_WhenOwnerRequestsOwnResume()
    {
        var db = CreateDb();
        var resume = await SeedOwnedResumeAsync(db, _userId);
        StubEngine(ResultWith(A7Fail(), B3Pass()));

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.RubricVersion.ShouldBe("1.0.0");
        result.Profile.ShouldBe("Ats");
        result.Verdicts.ShouldContain(v => v.CriterionId == "A7" && v.Verdict == "Fail");
        // Name is surfaced from the rubric's single source of truth, resolved by criterion id.
        result.Verdicts.ShouldContain(v => v.CriterionId == "A7" && v.Name == "Anti-klyschor");
        // No finding-status row seeded → no overlay.
        result.Verdicts.Single(v => v.CriterionId == "A7").UserStatus.ShouldBeNull();

        await _engine.Received(1).ReviewAsync(
            Arg.Any<CvReviewContext>(), RenderProfile.Ats, Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Ignorable (StyleOnly) projection (Fas 4b PR-8.4, CTO-bind Q1)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldMarkStyleOnlyCriterionIgnorable_AndContentCriterionNot()
    {
        var db = CreateDb();
        var resume = await SeedOwnedResumeAsync(db, _userId);
        // The canonical handler derives the ignorable set from the REAL rubric's StyleOnly ids
        // (E3/E4/E7/E8/B5/B8 in the committed rubric) — E3 is style-only, A7 (Anti-klyschor) is not.
        StubEngine(ResultWith(E3StyleWarn(), A7Fail()));

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        // §5-honesty gate: only style-only criteria may be ignored — mirrored 1:1 from the rubric data.
        result.Verdicts.Single(v => v.CriterionId == "E3").IsIgnorable.ShouldBeTrue();
        result.Verdicts.Single(v => v.CriterionId == "A7").IsIgnorable.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_ShouldPassCanonicalSourceContextToEngine()
    {
        var db = CreateDb();
        var resume = await SeedOwnedResumeAsync(db, _userId);
        CvReviewContext? captured = null;
        _engine.ReviewAsync(
                Arg.Do<CvReviewContext>(c => captured = c),
                Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(ResultWith(A7Fail())));

        await CreateSut(db).Handle(
            new ReviewResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        // The canonical adapter tags the input Source — this is the promoted/app-built arm (D8).
        captured!.Source.ShouldBe(CvReviewSourceKind.Canonical);
    }

    [Fact]
    public async Task Handle_ShouldPassVisualProfileToEngine_WhenProfileIsVisual()
    {
        var db = CreateDb();
        var resume = await SeedOwnedResumeAsync(db, _userId);
        StubEngine(ResultWith(A7Fail()));

        await CreateSut(db).Handle(
            new ReviewResumeQuery(resume.Id.Value, "Visual"), TestContext.Current.CancellationToken);

        await _engine.Received(1).ReviewAsync(
            Arg.Any<CvReviewContext>(), RenderProfile.Visual, Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Auth / not-found / cross-user — null returns
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = CreateDb();
        var resume = await SeedOwnedResumeAsync(db, _userId);

        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new ReviewResumeQueryHandler(db, anon, _engine, _rubricProvider, _failedAccess,
            TestFindingFingerprinter.Instance);

        var result = await sut.Handle(
            new ReviewResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = CreateDb(); // no JobSeeker for _userId

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndNotCallEngine_WhenResumeNotFound()
    {
        var db = CreateDb();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        await _engine.DidNotReceive().ReviewAsync(
            Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndLogCrossUserAttempt_WhenResumeBelongsToOtherUser()
    {
        var db = CreateDb();
        var otherResume = await SeedOwnedResumeAsync(db, Guid.NewGuid());
        // The requesting user has a job seeker but does not own the resume.
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(otherResume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "Resume", otherResume.Id.Value, _userId, Arg.Any<string>());
        await _engine.DidNotReceive().ReviewAsync(
            Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Status overlay merge (D2(e), CTO-bind PR-4 Q3)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldSurfaceUserStatus_WhenFreshResolvedRowExists()
    {
        var db = CreateDb();
        var matchingFingerprint = TestFindingFingerprinter.Compute(StubVersion, A7Fail());
        var resume = await SeedOwnedResumeAsync(db, _userId, r =>
            r.SetFindingStatus("1.0.0", "A7", ReviewFindingStatus.Resolved,
                matchingFingerprint, FakeDateTimeProvider.Default));
        StubEngine(ResultWith(A7Fail(), B3Pass()));

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        var a7 = result.Verdicts.Single(v => v.CriterionId == "A7");
        a7.UserStatus.ShouldBe("Resolved");
        // A fresh (non-stale) decision carries no staleness stamp.
        a7.UserStatusStaleAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldNotOverlay_WhenRowKeyedToAnotherRubricVersion()
    {
        var db = CreateDb();
        var resume = await SeedOwnedResumeAsync(db, _userId, r =>
            r.SetFindingStatus("2.0.0", "A7", ReviewFindingStatus.Resolved,
                TestFindingFingerprinter.Compute(RubricVersion.Parse("2.0.0"), A7Fail()),
                FakeDateTimeProvider.Default));
        StubEngine(ResultWith(A7Fail())); // result is version 1.0.0

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        // The 2.0.0 row never carries onto a 1.0.0 review (rubric-version key boundary).
        result.Verdicts.Single(v => v.CriterionId == "A7").UserStatus.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldNotCarryV110StatusOntoV120Review_WhenTheRubricMinorBumped()
    {
        // Fas 4b PR-5 CTO In-block fix 5 / ADR 0097 §5: the Ignored/Resolved invalidation axis IS
        // the rubric-version key. A status persisted under rubric_version "1.1.0" must NOT surface
        // on a review COMPUTED under v1.2.0 (the #654 minor bump: thresholds-as-data + styleOnly).
        // The v1.1 Ignored/Resolved reset to Open under v1.2 is BY DESIGN, not a regression (any
        // user-facing release note is a PR-8 concern). Sibling to
        // Handle_ShouldNotOverlay_WhenRowKeyedToAnotherRubricVersion, pinned on the concrete
        // 1.1.0 → 1.2.0 boundary this PR introduces.
        var db = CreateDb();
        var resume = await SeedOwnedResumeAsync(db, _userId, r =>
            r.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Resolved,
                TestFindingFingerprinter.Compute(RubricVersion.Parse("1.1.0"), A7Fail()),
                FakeDateTimeProvider.Default));
        // The review rides the SHIPPED v1.2.0 rubric version (stamped on every result).
        StubEngine(new CvReviewResult(
            RubricVersion.Parse("1.2.0"), RenderProfile.Ats, [], [A7Fail()], [], 1, 1));

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.RubricVersion.ShouldBe("1.2.0");
        // The 1.1.0 decision never carries onto a 1.2.0 review (rubric-version key boundary).
        result.Verdicts.Single(v => v.CriterionId == "A7").UserStatus.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldClearStaleResolvedRow_WhenFingerprintNoLongerMatches()
    {
        var db = CreateDb();
        // A shape-valid fingerprint that cannot be the current finding's digest.
        var mismatch = new string('0', 64);
        var resume = await SeedOwnedResumeAsync(db, _userId, r =>
        {
            r.SetFindingStatus("1.0.0", "A7", ReviewFindingStatus.Resolved, mismatch, FakeDateTimeProvider.Default);
            // A content change stamps staleness on the Resolved row.
            r.UpdateMasterContent(ResumeContent.Empty("Anna Andersson"),
                new FakeDateTimeProvider(FakeDateTimeProvider.Default.UtcNow.AddHours(1)));
        });
        StubEngine(ResultWith(A7Fail()));

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        // Resolved + stale + fingerprint no longer matches → the finding is gone → silently cleared,
        // never a fabricated lingering warning.
        result.Verdicts.Single(v => v.CriterionId == "A7").UserStatus.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldSurfaceStaleResolvedRow_WhenFingerprintStillMatches()
    {
        var db = CreateDb();
        var staleClock = new FakeDateTimeProvider(FakeDateTimeProvider.Default.UtcNow.AddHours(1));
        var matchingFingerprint = TestFindingFingerprinter.Compute(StubVersion, A7Fail());
        var resume = await SeedOwnedResumeAsync(db, _userId, r =>
        {
            r.SetFindingStatus("1.0.0", "A7", ReviewFindingStatus.Resolved,
                matchingFingerprint, FakeDateTimeProvider.Default);
            r.UpdateMasterContent(ResumeContent.Empty("Anna Andersson"), staleClock);
        });
        StubEngine(ResultWith(A7Fail()));

        var result = await CreateSut(db).Handle(
            new ReviewResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        var a7 = result.Verdicts.Single(v => v.CriterionId == "A7");
        // Resolved + stale + fingerprint STILL matches → "marked fixed, still present" → surfaced
        // with its staleness so the UI can prompt a re-review.
        a7.UserStatus.ShouldBe("Resolved");
        a7.UserStatusStaleAt.ShouldBe(staleClock.UtcNow);
    }
}
