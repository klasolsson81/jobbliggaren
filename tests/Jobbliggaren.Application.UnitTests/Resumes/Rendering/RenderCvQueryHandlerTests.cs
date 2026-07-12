using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Resumes.Improvement;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// Fas 4 STEG 10 (F4-10, ADR 0074) — the read handler that renders the OWNING job seeker's parsed
/// CV to a PDF. Mirrors <c>ReviewParsedResumeQueryHandlerTests</c>: resolve owner, FirstOrDefault
/// on db.ParsedResumes filtered by Id + JobSeekerId, null on not-found OR cross-user (logging via
/// <see cref="IFailedAccessLogger"/>), else render and map to <see cref="RenderedCvDto"/>. The
/// <see cref="ICvRenderer"/> is NSubstitute-mocked — the handler under test is the
/// ownership/cross-user/null orchestration + the DTO projection, NOT the QuestPDF internals
/// (CvRendererTests).
///
/// CA2012: stubbing ValueTask-returning ICvRenderer.RenderAsync is the known NSubstitute analyzer
/// false positive (parity ReviewParsedResumeQueryHandlerTests).
/// </summary>
#pragma warning disable CA2012
public class RenderCvQueryHandlerTests
{
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46];

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvRenderer _renderer = Substitute.For<ICvRenderer>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public RenderCvQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private RenderCvQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _renderer, _failedAccess);

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

    private void StubRenderer() =>
        _renderer.RenderAsync(
                Arg.Any<ParsedResume>(), Arg.Any<CvTemplateOptions>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ValueTask<RenderedCv>(
                new RenderedCv(PdfMagic, "application/pdf", ci.ArgAt<RenderProfile>(2), ResumeLanguage.Sv)));

    // ===============================================================
    // Happy path
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnDto_WhenOwnerRequestsOwnParsedResume()
    {
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId);
        StubRenderer();

        var result = await CreateSut(db).Handle(
            new RenderCvQuery(parsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.PdfBytes.ShouldBe(PdfMagic);
        result.ContentType.ShouldBe("application/pdf");
        result.Profile.ShouldBe("Ats");
        result.Language.ShouldBe("Sv");

        await _renderer.Received(1).RenderAsync(
            Arg.Any<ParsedResume>(), Arg.Any<CvTemplateOptions>(), RenderProfile.Ats, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPassVisualProfileToRenderer_WhenProfileIsVisual()
    {
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId);
        StubRenderer();

        var result = await CreateSut(db).Handle(
            new RenderCvQuery(parsed.Id.Value, "Visual"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result!.Profile.ShouldBe("Visual");
        await _renderer.Received(1).RenderAsync(
            Arg.Any<ParsedResume>(), Arg.Any<CvTemplateOptions>(), RenderProfile.Visual, Arg.Any<CancellationToken>());
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
        var sut = new RenderCvQueryHandler(db, anon, _renderer, _failedAccess);

        var result = await sut.Handle(
            new RenderCvQuery(parsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await CreateSut(db).Handle(
            new RenderCvQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndNotCallRenderer_WhenParsedResumeNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new RenderCvQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        await _renderer.DidNotReceive().RenderAsync(
            Arg.Any<ParsedResume>(), Arg.Any<CvTemplateOptions>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
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
            new RenderCvQuery(otherParsed.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, Arg.Any<string>());
        await _renderer.DidNotReceive().RenderAsync(
            Arg.Any<ParsedResume>(), Arg.Any<CvTemplateOptions>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }
}
