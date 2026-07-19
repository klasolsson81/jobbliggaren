using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Queries.PreviewCvImprovement;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Common.Security;
using Jobbliggaren.Application.UnitTests.Resumes.Improvement;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement.Queries;

/// <summary>
/// Fas 4b PR-7 (#656, ADR 0093 §D2) — the read-only preview: it MINTS the finding fingerprint the
/// client will echo to apply, composes the After through the SAME composer + FromFrame the apply
/// command uses, and returns a linearized post-apply text with personnummer REDACTED for display —
/// but it NEVER persists (no UpdateMasterContent, no status write). Owner/IDOR shape mirrors
/// <c>SuggestCvImprovementsQueryHandler</c>; the query surfaces failures as a <c>Result</c> (parity
/// LookupCompanyQuery's <c>IQuery&lt;Result&lt;T&gt;&gt;</c>), so an ungroundable input becomes a
/// typed Validation failure rather than a fabricated preview, and not-found becomes a NotFound
/// failure the central mapper renders 404. Canonical content is hydrated by
/// <see cref="FakeContentHydrationInterceptor"/> (AsNoTracking + InMemory re-materializes it null).
///
/// CA2012: stubbing the ValueTask-returning ICvReviewEngine.ReviewAsync is the known NSubstitute
/// analyzer false positive. SPEC-DRIVEN — RED until the query + FramePreviewDto + handler ship.
/// </summary>
#pragma warning disable CA2012
public class PreviewCvImprovementQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvReviewEngine _engine = Substitute.For<ICvReviewEngine>();
    private readonly IFrameProvider _frameProvider = Substitute.For<IFrameProvider>();
    private readonly IVerbMapper _verbMapper = Substitute.For<IVerbMapper>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly RubricVersion Version = RubricVersion.Parse("1.2.0");
    private const string Pnr = "811218-9876";
    private const string Mask = "******-****";

    public PreviewCvImprovementQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        _frameProvider.GetFrameCatalog().Returns(FrameFixtures.Catalog(
            FrameFixtures.SentenceLedde(), FrameFixtures.MeasureAntalPerPeriod()));
        _verbMapper.GetVerbMapping().Returns(FrameFixtures.VerbMappingWith("ledde", "skickade"));
    }

    private PreviewCvImprovementQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _engine, _frameProvider, _verbMapper, _failedAccess, TestFindingFingerprinter.Instance);

    private void StubReview(CvReviewResult review) =>
        _engine.ReviewAsync(Arg.Any<CvReviewContext>(), Arg.Any<RenderProfile>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CvReviewResult>(review));

    private static CvCriterionVerdict FailA2() =>
        CvCriterionVerdict.Assessed("A2", RubricCategory.Content, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(TextSpan.NotLocated, FrameFixtures.WeakLine.Length, FrameFixtures.WeakLine), null)]);

    private static CvReviewResult ResultWith(params CvCriterionVerdict[] verdicts) =>
        new(Version, RenderProfile.Ats, [], verdicts, [], verdicts.Length, verdicts.Length);

    // Canonical content the hydration interceptor supplies: the weak A2 summary line plus an
    // experience whose description carries a personnummer ELSEWHERE (to prove the redacted view).
    private static ResumeContent PreviewContent() =>
        new(new PersonalInfo("Klas Olsson", "klas@example.se", null, "Stockholm"),
            experiences: [new Experience("Acme AB", "Utvecklare", new DateOnly(2021, 1, 1), null, $"Tidigare projekt. Pnr {Pnr}.")],
            summary: FrameFixtures.WeakLine);

    private static Infrastructure.Persistence.AppDbContext CreateDb(ResumeContent content) =>
        TestAppDbContextFactory.Create(new FakeContentHydrationInterceptor(resumeContent: content));

    private static async Task<Resume> SeedResumeAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId, Action<Resume>? configure = null)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        configure?.Invoke(resume);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    private static PreviewCvImprovementQuery Query(Guid resumeId, IReadOnlyDictionary<string, string>? slots = null) =>
        new(resumeId, "A2", "sentence-ledde", slots ?? FrameFixtures.LeddeSlots());

    // ===============================================================
    // Happy path — minted fingerprint + redacted post-apply text
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnPreview_WhenOwnerRequestsAGroundedFrame()
    {
        var db = CreateDb(PreviewContent());
        var resume = await SeedResumeAsync(db, _userId);
        var verdict = FailA2();
        StubReview(ResultWith(verdict));

        var result = await CreateSut(db).Handle(Query(resume.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.CriterionId.ShouldBe("A2");
        dto.FrameId.ShouldBe("sentence-ledde");
        dto.Before.ShouldBe(FrameFixtures.WeakLine);
        dto.After.ShouldBe(FrameFixtures.LeddeAfter);
        // The fingerprint is MINTED server-side — the exact digest the apply command will re-derive.
        dto.FindingFingerprint.ShouldBe(TestFindingFingerprinter.Compute(Version, verdict));
        dto.RubricVersion.ShouldBe("1.2.0");
    }

    [Fact]
    public async Task Handle_ShouldRedactPersonnummerInPostApplyLinearText_WhileShowingTheRewrite()
    {
        // Anchor: the mask is the real scanner-masked form (anti-stale).
        PersonnummerScanner.Scan(Pnr).ShouldHaveSingleItem().Masked.ShouldBe(Mask);

        var db = CreateDb(PreviewContent());
        var resume = await SeedResumeAsync(db, _userId);
        StubReview(ResultWith(FailA2()));

        var result = await CreateSut(db).Handle(Query(resume.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var text = result.Value.PostApplyLinearText;
        text.ShouldContain(FrameFixtures.LeddeAfter, Case.Sensitive, "the composed rewrite is visible in the preview text.");
        text.ShouldContain(Mask, Case.Sensitive, "a personnummer elsewhere in the CV is masked in the preview.");
        text.ShouldNotContain(Pnr);
    }

    [Fact]
    public async Task Handle_ShouldNotPersist_WhenComposingThePreview()
    {
        // Non-persistence pin: a pre-existing Resolved finding stays NON-stale, which it could not
        // if the preview had run UpdateMasterContent (that stamps StaleAt on resolved rows).
        var db = CreateDb(PreviewContent());
        var resume = await SeedResumeAsync(db, _userId, r =>
            r.SetFindingStatus(Version.ToString(), "B5", ReviewFindingStatus.Resolved,
                new string('a', 64), FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue());
        StubReview(ResultWith(FailA2()));

        var result = await CreateSut(db).Handle(Query(resume.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var reloaded = await db.Resumes.AsNoTracking().Include(r => r.FindingStatuses)
            .FirstAsync(r => r.Id == resume.Id, CancellationToken.None);
        var b5 = reloaded.FindingStatuses.ShouldHaveSingleItem();
        b5.CriterionId.ShouldBe("B5");
        b5.StaleAt.ShouldBeNull("preview must not run UpdateMasterContent, which would stamp staleness.");
    }

    // ===============================================================
    // Validation + IDOR failures surfaced as Result
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnFrameSlotNotGrounded_WhenANounSlotIsUngrounded()
    {
        var db = CreateDb(PreviewContent());
        var resume = await SeedResumeAsync(db, _userId);
        StubReview(ResultWith(FailA2()));
        var slots = FrameFixtures.LeddeSlots();
        slots["del4"] = "obefintlig";

        var result = await CreateSut(db).Handle(Query(resume.Id.Value, slots), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FrameSlotNotGrounded");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFoundFailure_WhenResumeNotFound_NoLog()
    {
        var db = CreateDb(PreviewContent());
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        StubReview(ResultWith(FailA2()));

        var result = await CreateSut(db).Handle(Query(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFoundFailureAndLogCrossUserAttempt_WhenResumeBelongsToOtherUser()
    {
        var db = CreateDb(PreviewContent());
        var otherResume = await SeedResumeAsync(db, Guid.NewGuid());
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(CancellationToken.None);
        StubReview(ResultWith(FailA2()));

        var result = await CreateSut(db).Handle(Query(otherResume.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        _failedAccess.Received(1).LogCrossUserAttempt(
            "Resume", otherResume.Id.Value, _userId, "PreviewCvImprovement");
    }
}
