using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4 STEG 9 (F4-9, ADR 0074) — the read handler that runs the deterministic CV review
/// for the OWNING job seeker. Mirrors <c>GetResumeByIdQueryHandler</c> EXACTLY: resolve the
/// owner JobSeekerId from <see cref="ICurrentUser"/>, FirstOrDefault on db.ParsedResumes
/// filtered by Id + JobSeekerId, return null on not-found OR cross-user (logging the
/// cross-user attempt via <see cref="IFailedAccessLogger"/>), else run the engine and map to
/// <see cref="CvReviewDto"/>.
///
/// The <see cref="ICvReviewEngine"/> is NSubstitute-mocked — the handler under test is the
/// ownership/cross-user/null-return orchestration, NOT the engine internals (those are
/// CvReviewEngineTests). Since Fas 4b PR-4 the handler builds the unified adapter input
/// (CvReviewContext.FromParsed) itself, so the EF-Ignore'd Content is hydrated on
/// materialization by <see cref="FakeContentHydrationInterceptor"/> (the production
/// decryption interceptor's test double; InMemory cannot decrypt the Form-B shadow).
///
/// SPEC-DRIVEN. RED until the query + handler + DTO + ICvReviewEngine ship.
///
/// CA2012: stubbing ValueTask-returning ICvReviewEngine.ReviewAsync is the known NSubstitute
/// analyzer false positive (parity ImportResumeCommandHandlerTests).
/// </summary>
#pragma warning disable CA2012
public class ReviewParsedResumeQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvReviewEngine _engine = Substitute.For<ICvReviewEngine>();
    // Real rubric provider (committed asset) so the criterionId→Name heading lookup the handler
    // performs resolves against the golden source (A1 → "Mätbara resultat" etc.).
    private readonly IRubricProvider _rubricProvider = CvReviewFixtures.RealRubricProvider();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public ReviewParsedResumeQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private ReviewParsedResumeQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _engine, _rubricProvider, _failedAccess);

    private static ParsedResumeContent TestContent() => new(
        new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
        profile: "Erfaren backend-utvecklare.",
        experience: [new ParsedExperience("Backend-utvecklare", "Acme AB", "2021–2024", "raw")]);

    // Fas 4b PR-4 (ADR 0093 §D8): the handler now builds the unified CvReviewContext
    // (CvReviewContext.FromParsed) BEFORE the mocked engine, so it dereferences the
    // EF-Ignore'd Content that only the decryption interceptor populates — hydrate it
    // on materialization exactly like production does (FakeContentHydrationInterceptor;
    // the real decrypt path stays proven by the Api integration tests).
    private static Infrastructure.Persistence.AppDbContext CreateDb() =>
        TestAppDbContextFactory.Create(new FakeContentHydrationInterceptor(parsedContent: TestContent()));

    private static ParsedResume BuildParsedResume(JobSeekerId owner)
    {
        return ParsedResume.Create(
            owner, "CV_Anna.pdf", "application/pdf", ResumeLanguage.Sv,
            TestContent(), "Anna Andersson\nLedde teamet.", CvReviewFixtures.ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], CvReviewFixtures.FixedClock.Default).Value;
    }

    private static async Task<(ParsedResume Parsed, JobSeeker Owner)> SeedOwnedAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = BuildParsedResume(seeker.Id);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (parsed, seeker);
    }

    private void StubEngine(CvReviewResult result) =>
        _engine.ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(result));

    // The engine returns the rich Application result; the handler maps it to the transport
    // CvReviewDto. A RICH result (both evidence channels + assessed + NotAssessed + a category
    // + a critical fail) so the happy-path test also exercises the full CvReviewResult→DTO
    // projection (CvReviewDtoMapper) — the engine's verdict LOGIC is covered in CvReviewEngineTests.
    private static CvReviewResult SampleResult()
    {
        var spanVerdict = CvCriterionVerdict.Assessed(
            "A1", RubricCategory.Content, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(0, 5, "Ledde"), "starkt verb")]);
        var structuralVerdict = CvCriterionVerdict.Assessed(
            "B3", RubricCategory.Structure, CriterionVerdict.Pass,
            [new StructuralEvidence("kontaktsektion komplett")]);
        var notAssessed = CvCriterionVerdict.NotAssessed("A5", RubricCategory.Content, "ej bedömt v1");

        var verdicts = new[] { spanVerdict, structuralVerdict, notAssessed };
        var category = new CvCategoryResult(
            RubricCategory.Content, PassCount: 0, WarnCount: 0, FailCount: 1, NotAssessedCount: 1,
            ScoreBandLabel.NeedsRework, [spanVerdict, notAssessed]);

        return new CvReviewResult(
            RubricVersion.Parse("1.0.0"), RenderProfile.Ats,
            [category], verdicts, CriticalFails: [spanVerdict], AssessedCount: 2, TotalCount: 3);
    }

    // ===============================================================
    // Happy path
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnDto_WhenOwnerRequestsOwnParsedResume()
    {
        var db = CreateDb();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);
        StubEngine(SampleResult());

        var result = await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(parsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        // The CvReviewResult → CvReviewDto projection: enums→names, version→string, evidence tagged.
        result.RubricVersion.ShouldBe("1.0.0");
        result.Profile.ShouldBe("Ats");
        result.AssessedCount.ShouldBe(2);
        result.TotalCount.ShouldBe(3);
        result.Categories.Count.ShouldBe(1);
        result.Categories[0].Band.ShouldBe("NeedsRework");
        result.CriticalFails.Count.ShouldBe(1);
        result.Verdicts.ShouldContain(v => v.Verdict == "Fail"
            && v.Evidence.Any(e => e.Kind == "TextSpan" && e.Quote == "Ledde"));
        result.Verdicts.ShouldContain(v => v.Evidence.Any(e => e.Kind == "Structural"
            && e.Observation == "kontaktsektion komplett"));
        result.Verdicts.ShouldContain(v => v.Verdict == "NotAssessed" && v.NotAssessedReason == "ej bedömt v1");
        // Name (the human rubric heading) is surfaced from the rubric's single source of truth,
        // resolved by criterion id — the UI leads with this, not the cryptic "A1".
        result.Verdicts.ShouldContain(v => v.CriterionId == "A1" && v.Name == "Mätbara resultat");
        result.Verdicts.ShouldContain(v => v.CriterionId == "B3" && v.Name == "Kontaktuppgifter kompletta");

        await _engine.Received(1).ReviewAsync(
            Arg.Any<CvReviewContext>(), RenderProfile.Ats, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPassParsedVisualProfileToEngine_WhenProfileIsVisual()
    {
        var db = CreateDb();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);
        StubEngine(SampleResult());

        await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(parsed.Id.Value, "Visual"), TestContext.Current.CancellationToken);

        await _engine.Received(1).ReviewAsync(
            Arg.Any<CvReviewContext>(), RenderProfile.Visual, Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Ignorable (StyleOnly) projection parity (Fas 4b PR-8.4, CTO-bind Q1)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldPopulateIsIgnorableFromRubric_EvenThoughStagingRendersNoControls()
    {
        // IsIgnorable is a STATIC criterion property, so the staging review must carry it
        // identically to the canonical one (the field's meaning must not vary by path, even
        // though the staging panel renders no status controls). E3 "Typografisk konsekvens"
        // is a StyleOnly criterion in the committed rubric; A1 "Mätbara resultat" is not.
        var db = CreateDb();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);
        var e3 = CvCriterionVerdict.Assessed(
            "E3", RubricCategory.VisualQuality, CriterionVerdict.Warn,
            [new StructuralEvidence("ojämn typografi")]);
        var a1 = CvCriterionVerdict.Assessed(
            "A1", RubricCategory.Content, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(0, 5, "Ledde"), "starkt verb")]);
        StubEngine(new CvReviewResult(
            RubricVersion.Parse("1.0.0"), RenderProfile.Ats, [], [e3, a1], [], AssessedCount: 2, TotalCount: 2));

        var result = await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(parsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.Verdicts.Single(v => v.CriterionId == "E3").IsIgnorable.ShouldBeTrue();
        result.Verdicts.Single(v => v.CriterionId == "A1").IsIgnorable.ShouldBeFalse();
    }

    // ===============================================================
    // Auth / not-found / cross-user — null returns
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = CreateDb();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new ReviewParsedResumeQueryHandler(db, anon, _engine, _rubricProvider, _failedAccess);

        var result = await sut.Handle(
            new ReviewParsedResumeQuery(parsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = CreateDb(); // no JobSeeker for _userId

        var result = await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndNotCallEngine_WhenParsedResumeNotFound()
    {
        var db = CreateDb();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        await _engine.DidNotReceive().ReviewAsync(
            Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndLogCrossUserAttempt_WhenParsedResumeBelongsToOtherUser()
    {
        var db = CreateDb();
        // Another user's parsed resume.
        var (otherParsed, _) = await SeedOwnedAsync(db, Guid.NewGuid());
        // The requesting user has a job seeker but does not own the artifact.
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(otherParsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, Arg.Any<string>());
        await _engine.DidNotReceive().ReviewAsync(
            Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }
}
