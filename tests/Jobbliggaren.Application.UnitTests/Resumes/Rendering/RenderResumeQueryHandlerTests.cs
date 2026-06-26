using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResume;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// TD-112 / #202 — the read handler that renders the OWNING job seeker's promoted Resume to a PDF.
/// Mirrors <c>RenderCvQueryHandlerTests</c> + <c>GetResumeByIdQueryHandlerTests</c>: resolve owner,
/// FirstOrDefault on db.Resumes (Versions included) filtered by Id + JobSeekerId, null on
/// not-found OR cross-user (logging via <see cref="IFailedAccessLogger"/>), else render the Master
/// content + the Resume language and map to <see cref="RenderedCvDto"/>. The
/// <see cref="ICvRenderer"/> is NSubstitute-mocked — the handler under test is the
/// ownership/cross-user/null orchestration + the DTO projection, NOT the QuestPDF internals
/// (CvRendererTests).
///
/// CA2012: stubbing ValueTask-returning ICvRenderer.RenderAsync is the known NSubstitute analyzer
/// false positive (parity RenderCvQueryHandlerTests).
/// </summary>
#pragma warning disable CA2012
public class RenderResumeQueryHandlerTests
{
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46];

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvRenderer _renderer = Substitute.For<ICvRenderer>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public RenderResumeQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private RenderResumeQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _renderer, _failedAccess);

    private static async Task<Resume> SeedOwnedResumeAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var resume = Resume.Create(seeker.Id, "Mitt CV", "Anna Andersson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return resume;
    }

    private void StubRenderer() =>
        _renderer.RenderAsync(
                Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ValueTask<RenderedCv>(
                new RenderedCv(PdfMagic, "application/pdf", ci.ArgAt<RenderProfile>(2), ResumeLanguage.Sv)));

    // ===============================================================
    // Happy path
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnDto_WhenOwnerRequestsOwnResume()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedOwnedResumeAsync(db, _userId);
        StubRenderer();

        var result = await CreateSut(db).Handle(
            new RenderResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.PdfBytes.ShouldBe(PdfMagic);
        result.ContentType.ShouldBe("application/pdf");
        result.Profile.ShouldBe("Ats");
        result.Language.ShouldBe("Sv");

        await _renderer.Received(1).RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), RenderProfile.Ats, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPassVisualProfileToRenderer_WhenProfileIsVisual()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedOwnedResumeAsync(db, _userId);
        StubRenderer();

        var result = await CreateSut(db).Handle(
            new RenderResumeQuery(resume.Id.Value, "Visual"), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result!.Profile.ShouldBe("Visual");
        await _renderer.Received(1).RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), RenderProfile.Visual, Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Auth / not-found / cross-user — null returns
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedOwnedResumeAsync(db, _userId);

        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new RenderResumeQueryHandler(db, anon, _renderer, _failedAccess);

        var result = await sut.Handle(
            new RenderResumeQuery(resume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await CreateSut(db).Handle(
            new RenderResumeQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndNotCallRenderer_WhenResumeNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new RenderResumeQuery(Guid.NewGuid(), "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        await _renderer.DidNotReceive().RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndLogCrossUserAttempt_WhenResumeBelongsToOtherUser()
    {
        var db = TestAppDbContextFactory.Create();
        var otherResume = await SeedOwnedResumeAsync(db, Guid.NewGuid());
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new RenderResumeQuery(otherResume.Id.Value, "Ats"), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "Resume", otherResume.Id.Value, _userId, Arg.Any<string>());
        await _renderer.DidNotReceive().RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }
}
