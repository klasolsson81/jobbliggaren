using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResumePreview;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// Fas 4b PR-8b 8b.3 (CTO-bind Q1 Variant B) — the ephemeral-preview handler that renders the OWNING
/// job seeker's promoted Resume with UNSAVED template options. Mirrors
/// <c>RenderResumeQueryHandlerTests</c>: the <see cref="ICvRenderer"/> is NSubstitute-mocked, so the
/// unit under test is the ownership/cross-user/null orchestration + the ephemeral option composition
/// (the requested four override the persisted, the persisted PHOTO is preserved, the profile is
/// always Visual), NOT the QuestPDF internals (CvRendererTests).
///
/// CA2012: stubbing ValueTask-returning ICvRenderer.RenderAsync is the known NSubstitute analyzer
/// false positive (parity RenderResumeQueryHandlerTests).
/// </summary>
#pragma warning disable CA2012
public class RenderResumePreviewQueryHandlerTests
{
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46];

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvRenderer _renderer = Substitute.For<ICvRenderer>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public RenderResumePreviewQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private RenderResumePreviewQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
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
                Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), Arg.Any<CvTemplateOptions>(),
                Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ValueTask<RenderedCv>(
                new RenderedCv(PdfMagic, "application/pdf", ci.ArgAt<RenderProfile>(3), ResumeLanguage.Sv)));

    private static RenderResumePreviewQuery Query(
        Guid resumeId,
        CvTemplate? template = null,
        CvAccentColor? accent = null,
        CvFontPair? font = null,
        CvDensity? density = null) =>
        new(resumeId,
            (template ?? CvTemplate.MorkPanel).Name,
            (accent ?? CvAccentColor.WineRed).Name,
            (font ?? CvFontPair.Classic).Name,
            (density ?? CvDensity.Airy).Name);

    // ===============================================================
    // Happy path + ephemeral composition
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnDto_WhenOwnerRequestsOwnResume()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedOwnedResumeAsync(db, _userId);
        StubRenderer();

        var result = await CreateSut(db).Handle(
            Query(resume.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.PdfBytes.ShouldBe(PdfMagic);
        result.ContentType.ShouldBe("application/pdf");
        result.Language.ShouldBe("Sv");
    }

    [Fact]
    public async Task Handle_ShouldAlwaysRenderVisual_NeverAts()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedOwnedResumeAsync(db, _userId);
        StubRenderer();

        var result = await CreateSut(db).Handle(
            Query(resume.Id.Value), TestContext.Current.CancellationToken);

        // The four options move nothing on an Ats render, so the preview is Visual by construction.
        result!.Profile.ShouldBe("Visual");
        await _renderer.Received(1).RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), Arg.Any<CvTemplateOptions>(),
            RenderProfile.Visual, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRenderTheRequestedEphemeralOptions_NotThePersistedOnes()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedOwnedResumeAsync(db, _userId);

        // Persist one set…
        resume.ChangeTemplateOptions(
            new CvTemplateOptions(
                CvTemplate.Klar, CvAccentColor.NavyBlue, CvFontPair.Modern,
                CvDensity.Normal, PhotoEnabled: false, CvPhotoShape.Circle),
            FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        StubRenderer();

        // …preview a DIFFERENT set. A bug that rendered persisted options (or a Default) would not match.
        await CreateSut(db).Handle(
            Query(resume.Id.Value, CvTemplate.MorkPanel, CvAccentColor.WineRed, CvFontPair.Classic, CvDensity.Airy),
            TestContext.Current.CancellationToken);

        var expected = new CvTemplateOptions(
            CvTemplate.MorkPanel, CvAccentColor.WineRed, CvFontPair.Classic,
            CvDensity.Airy, PhotoEnabled: false, CvPhotoShape.Circle);
        await _renderer.Received(1).RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), expected,
            RenderProfile.Visual, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPreserveThePersistedPhotoConfig_WhilstOverridingTheFourVisualOptions()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedOwnedResumeAsync(db, _userId);

        // Persist a photo config (photo is not on the write contract, but the domain VO can hold it —
        // the preview must NEVER drop/alter it; it composes only the four visual members over it).
        resume.ChangeTemplateOptions(
            new CvTemplateOptions(
                CvTemplate.Klar, CvAccentColor.NavyBlue, CvFontPair.Modern,
                CvDensity.Normal, PhotoEnabled: true, CvPhotoShape.Square),
            FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        StubRenderer();

        await CreateSut(db).Handle(
            Query(resume.Id.Value, CvTemplate.MorkPanel, CvAccentColor.Graphite, CvFontPair.Classic, CvDensity.Compact),
            TestContext.Current.CancellationToken);

        var expected = new CvTemplateOptions(
            CvTemplate.MorkPanel, CvAccentColor.Graphite, CvFontPair.Classic,
            CvDensity.Compact, PhotoEnabled: true, CvPhotoShape.Square);
        await _renderer.Received(1).RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), expected,
            RenderProfile.Visual, Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Auth / not-found / cross-user — null returns (parity RenderResume)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedOwnedResumeAsync(db, _userId);

        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new RenderResumePreviewQueryHandler(db, anon, _renderer, _failedAccess);

        var result = await sut.Handle(Query(resume.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await CreateSut(db).Handle(
            Query(Guid.NewGuid()), TestContext.Current.CancellationToken);

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
            Query(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        await _renderer.DidNotReceive().RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), Arg.Any<CvTemplateOptions>(),
            Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
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
            Query(otherResume.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "Resume", otherResume.Id.Value, _userId, Arg.Any<string>());
        await _renderer.DidNotReceive().RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), Arg.Any<CvTemplateOptions>(),
            Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Defense-in-depth: a direct Send with a bad name (bypassing the validator) degrades to null
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNullAndNotCallRenderer_WhenAnOptionNameIsUnknown()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedOwnedResumeAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            new RenderResumePreviewQuery(
                resume.Id.Value, "NotATemplate", CvAccentColor.NavyBlue.Name,
                CvFontPair.Modern.Name, CvDensity.Normal.Name),
            TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        await _renderer.DidNotReceive().RenderAsync(
            Arg.Any<ResumeContent>(), Arg.Any<ResumeLanguage>(), Arg.Any<CvTemplateOptions>(),
            Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>());
    }
}
