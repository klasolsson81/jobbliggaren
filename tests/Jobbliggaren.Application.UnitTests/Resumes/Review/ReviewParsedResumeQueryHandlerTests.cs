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
/// CvReviewEngineTests). The seeded ParsedResume is read back IN THE SAME CONTEXT so the
/// EF-Ignore'd Content shadow does not need re-materialization (InMemory cannot decrypt the
/// Form-B shadow — parity with the GetResumeByIdQueryHandler resume-found note).
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
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public ReviewParsedResumeQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private ReviewParsedResumeQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _engine, _failedAccess);

    private static ParsedResume BuildParsedResume(JobSeekerId owner)
    {
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience: [new ParsedExperience("Backend-utvecklare", "Acme AB", "2021–2024", "raw")]);

        return ParsedResume.Create(
            owner, "CV_Anna.pdf", "application/pdf", ResumeLanguage.Sv,
            content, "Anna Andersson\nLedde teamet.", CvReviewFixtures.ConfidentConfidence(),
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
        _engine.ReviewAsync(Arg.Any<ParsedResume>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(result));

    // The engine returns the rich Application result; the handler maps it to the transport
    // CvReviewDto. This handler test exercises the ownership / cross-user / null-return
    // orchestration, so a minimal result suffices (the engine's verdicts are covered in
    // CvReviewEngineTests; the DTO projection in the integration tests).
    private static CvReviewResult SampleResult() =>
        new(RubricVersion.Parse("1.0.0"), RenderProfile.Ats, [], [], [], AssessedCount: 0, TotalCount: 0);

    // ===============================================================
    // Happy path
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnDto_WhenOwnerRequestsOwnParsedResume()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);
        StubEngine(SampleResult());

        var result = await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(parsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        await _engine.Received(1).ReviewAsync(
            Arg.Any<ParsedResume>(), RenderProfile.Ats, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPassParsedVisualProfileToEngine_WhenProfileIsVisual()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);
        StubEngine(SampleResult());

        await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(parsed.Id.Value, "Visual"), TestContext.Current.CancellationToken);

        await _engine.Received(1).ReviewAsync(
            Arg.Any<ParsedResume>(), RenderProfile.Visual, Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Auth / not-found / cross-user — null returns
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new ReviewParsedResumeQueryHandler(db, anon, _engine, _failedAccess);

        var result = await sut.Handle(
            new ReviewParsedResumeQuery(parsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = TestAppDbContextFactory.Create(); // no JobSeeker for _userId

        var result = await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndNotCallEngine_WhenParsedResumeNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new ReviewParsedResumeQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        await _engine.DidNotReceive().ReviewAsync(
            Arg.Any<ParsedResume>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndLogCrossUserAttempt_WhenParsedResumeBelongsToOtherUser()
    {
        var db = TestAppDbContextFactory.Create();
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
            Arg.Any<ParsedResume>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }
}
