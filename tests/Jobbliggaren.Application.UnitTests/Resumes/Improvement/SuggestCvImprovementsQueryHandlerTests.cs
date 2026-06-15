using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Queries.SuggestCvImprovements;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Fas 4 STEG 10 (F4-10, ADR 0074) — the read handler that runs the deterministic CV-improve
/// pass for the OWNING job seeker. Mirrors <c>ReviewParsedResumeQueryHandlerTests</c>: resolve
/// owner from <see cref="ICurrentUser"/>, FirstOrDefault on db.ParsedResumes filtered by Id +
/// JobSeekerId, null on not-found OR cross-user (logging via <see cref="IFailedAccessLogger"/>),
/// else run the F4-9 review THEN the improve engine (CTO Q2) and map to <see cref="CvImprovementDto"/>.
///
/// Both engines are NSubstitute-mocked — the handler under test is the ownership/cross-user/null
/// orchestration + the review→improve composition + the DTO projection (the engine verdict LOGIC
/// is CvImprovementEngineTests). A RICH improvement result (both provenance arms + a text-span and
/// a structural change) so the happy path also exercises the full CvImprovementResult→DTO mapping.
///
/// CA2012: stubbing ValueTask-returning engine methods is the known NSubstitute analyzer false
/// positive (parity ReviewParsedResumeQueryHandlerTests).
/// </summary>
#pragma warning disable CA2012
public class SuggestCvImprovementsQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvReviewEngine _reviewEngine = Substitute.For<ICvReviewEngine>();
    private readonly ICvImprovementEngine _improvementEngine = Substitute.For<ICvImprovementEngine>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public SuggestCvImprovementsQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private SuggestCvImprovementsQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _reviewEngine, _improvementEngine, _failedAccess);

    private static ParsedResume BuildParsedResume(JobSeekerId owner)
    {
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience: [new ParsedExperience("Backend-utvecklare", "Acme AB", "2021–2024", "raw")]);

        return ParsedResume.Create(
            owner, "CV_Anna.pdf", "application/pdf", ResumeLanguage.Sv,
            content, "Anna Andersson\nLedde teamet.", CvImprovementFixtures.ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], CvImprovementFixtures.FixedClock.Default).Value;
    }

    private static async Task<ParsedResume> SeedOwnedAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = BuildParsedResume(seeker.Id);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return parsed;
    }

    private void StubReview() =>
        _reviewEngine.ReviewAsync(Arg.Any<ParsedResume>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(
                new CvReviewResult(RubricVersion.Parse("1.0.0"), RenderProfile.Ats, [], [], [], 0, 0)));

    private void StubImprove(CvImprovementResult result) =>
        _improvementEngine.SuggestAsync(
            Arg.Any<ParsedResume>(), Arg.Any<CvReviewResult?>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvImprovementResult>(result));

    // A rich result: a KB-arm text replacement + a structural-arm pure removal, so the happy path
    // exercises both ChangeProvenance arms + both evidence channels in CvImprovementDtoMapper.
    private static CvImprovementResult SampleResult()
    {
        var kb = ProposedChange.FromKnowledgeBank(
            "cliche:0", ProposedChangeKind.ClicheReplacement, RubricCategory.Content, "A7",
            new TextSpanEvidence(new TextSpan(0, 11, "Brinner för"), "klyscha"),
            new ProposedReplacement("Brinner för", "Beskriv ett konkret projekt"),
            "Tom passion-signal",
            new KnowledgeBankProvenance("cliche-list", "1", "Brinner för"),
            "Beskriv ett konkret projekt");

        var structural = ProposedChange.FromStructuralOp(
            "personnummer:0", ProposedChangeKind.PersonnummerStrip, RubricCategory.Structure, "B4",
            new StructuralEvidence("Personnummer hittat (1 förekomst(er))."),
            replacement: null,
            new StructuralOperation(StructuralTransformKind.RemovePersonnummer, "1 personnummer-förekomst(er)"),
            "Ta bort personnummer",
            new StructuralTransformProvenance(StructuralTransformKind.RemovePersonnummer),
            pureTransform: null);

        return new CvImprovementResult("1", "1", RubricVersion.Parse("1.0.0"), RenderProfile.Ats, [kb, structural]);
    }

    // ===============================================================
    // Happy path — runs review then improve, maps both provenance arms
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnDto_WhenOwnerRequestsOwnParsedResume()
    {
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId);
        StubReview();
        StubImprove(SampleResult());

        var result = await CreateSut(db).Handle(
            new SuggestCvImprovementsQuery(parsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.ClicheListVersion.ShouldBe("1");
        result.VerbMappingVersion.ShouldBe("1");
        result.RubricVersion.ShouldBe("1.0.0");
        result.Profile.ShouldBe("Ats");
        result.Changes.Count.ShouldBe(2);

        var kb = result.Changes.Single(c => c.Kind == "ClicheReplacement");
        kb.Provenance.Kind.ShouldBe("KnowledgeBank");
        kb.Provenance.Key.ShouldBe("Brinner för");
        kb.Evidence.Kind.ShouldBe("TextSpan");
        kb.Evidence.Quote.ShouldBe("Brinner för");
        kb.Replacement.ShouldNotBeNull();
        kb.Replacement!.After.ShouldBe("Beskriv ett konkret projekt");
        kb.Operation.ShouldBeNull();

        var structural = result.Changes.Single(c => c.Kind == "PersonnummerStrip");
        structural.Provenance.Kind.ShouldBe("StructuralTransform");
        structural.Provenance.Transform.ShouldBe("RemovePersonnummer");
        structural.Evidence.Kind.ShouldBe("Structural");
        structural.Evidence.Observation.ShouldBe("Personnummer hittat (1 förekomst(er)).");
        structural.Replacement.ShouldBeNull();
        structural.Operation.ShouldNotBeNull();

        await _reviewEngine.Received(1).ReviewAsync(
            Arg.Any<ParsedResume>(), RenderProfile.Ats, Arg.Any<CancellationToken>());
        await _improvementEngine.Received(1).SuggestAsync(
            Arg.Any<ParsedResume>(), Arg.Any<CvReviewResult?>(), RenderProfile.Ats, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPassVisualProfileToBothEngines_WhenProfileIsVisual()
    {
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId);
        StubReview();
        StubImprove(SampleResult());

        await CreateSut(db).Handle(
            new SuggestCvImprovementsQuery(parsed.Id.Value, "Visual"), TestContext.Current.CancellationToken);

        await _reviewEngine.Received(1).ReviewAsync(
            Arg.Any<ParsedResume>(), RenderProfile.Visual, Arg.Any<CancellationToken>());
        await _improvementEngine.Received(1).SuggestAsync(
            Arg.Any<ParsedResume>(), Arg.Any<CvReviewResult?>(), RenderProfile.Visual, Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Auth / not-found / cross-user — null returns
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId);

        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new SuggestCvImprovementsQueryHandler(db, anon, _reviewEngine, _improvementEngine, _failedAccess);

        var result = await sut.Handle(
            new SuggestCvImprovementsQuery(parsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await CreateSut(db).Handle(
            new SuggestCvImprovementsQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndNotCallEngines_WhenParsedResumeNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new SuggestCvImprovementsQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        await _improvementEngine.DidNotReceive().SuggestAsync(
            Arg.Any<ParsedResume>(), Arg.Any<CvReviewResult?>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndLogCrossUserAttempt_WhenParsedResumeBelongsToOtherUser()
    {
        var db = TestAppDbContextFactory.Create();
        var otherParsed = await SeedOwnedAsync(db, Guid.NewGuid());
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new SuggestCvImprovementsQuery(otherParsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, Arg.Any<string>());
        await _improvementEngine.DidNotReceive().SuggestAsync(
            Arg.Any<ParsedResume>(), Arg.Any<CvReviewResult?>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }
}
