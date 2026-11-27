using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;
using Jobbliggaren.Application.Resumes.Queries.GetResumes;
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

// #2353: SetResumeLanguage re-reviews a saved CV with the other language's lists and dictionary
// (ADR 0191), so a language-dependent criterion can carry a different finding afterwards. The stubbed
// engine returns what the real one does for such a criterion: another C7 span under En than under Sv.
// CA2012: stubbing the ValueTask-returning ReviewAsync is the known NSubstitute analyzer false positive.
#pragma warning disable CA2012
public class LanguageSwitchFindingStatusTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICvReviewEngine _engine = Substitute.For<ICvReviewEngine>();
    private readonly IRubricProvider _rubricProvider = CvReviewFixtures.RealRubricProvider();
    private readonly Guid _userId = Guid.NewGuid();

    public LanguageSwitchFindingStatusTests()
    {
        _currentUser.UserId.Returns(_userId);
        StubEngineFor(ResumeLanguage.Sv, C7Warn("Ledde"), A1Fail());
        StubEngineFor(ResumeLanguage.En, C7Warn("teamet"), A1Fail());
    }

    private RubricVersion Version => _rubricProvider.GetRubric().Version;

    private static CvCriterionVerdict C7Warn(string flaggedWord) =>
        CvCriterionVerdict.Assessed("C7", RubricCategory.Language, CriterionVerdict.Warn,
            [new TextSpanEvidence(new TextSpan(0, flaggedWord.Length, flaggedWord), "möjligt stavfel")]);

    private static CvCriterionVerdict A1Fail() =>
        CvCriterionVerdict.Assessed("A1", RubricCategory.Content, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(0, 13, "Ledde teamet."), "inget mätbart resultat")]);

    private void StubEngineFor(ResumeLanguage language, params CvCriterionVerdict[] verdicts) =>
        _engine.ReviewAsync(
                Arg.Is<CvReviewContext>(c => c.Language == language),
                Arg.Any<RenderProfile>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<CvReviewResult>(new CvReviewResult(
                Version, call.ArgAt<RenderProfile>(1), [], verdicts, [], verdicts.Length, verdicts.Length)));

    [Fact]
    public async Task AfterALanguageChange_AResolvedMarkStaysOnlyOnTheSameFinding_AndTheHubCountsTheReplacement()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = FakeDateTimeProvider.Default;
        var db = TestAppDbContextFactory.Create(
            new FakeContentHydrationInterceptor(resumeContent: ResumeContent.Empty("Anna Andersson")));
        var seeker = JobSeeker.Register(_userId, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        db.JobSeekers.Add(seeker);
        var resume = Resume.Create(seeker.Id, "Mitt CV", "Anna Andersson", clock).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);
        resume.Language.ShouldBe(ResumeLanguage.Sv);

        // The first review seeds the ledger; the owner then marks both findings fixed.
        var reconciler = new ResumeReviewReconciler(_engine, TestFindingFingerprinter.Instance, clock);
        await reconciler.ReconcileAsync(resume, autoResolveCriteria: null, ct);
        resume.SetFindingStatus(Version.ToString(), "C7", ReviewFindingStatus.Resolved,
            TestFindingFingerprinter.Compute(Version, C7Warn("Ledde")), clock).IsSuccess.ShouldBeTrue();
        resume.SetFindingStatus(Version.ToString(), "A1", ReviewFindingStatus.Resolved,
            TestFindingFingerprinter.Compute(Version, A1Fail()), clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var changed = await new SetResumeLanguageCommandHandler(
                db, _currentUser, clock, Substitute.For<IFailedAccessLogger>(), reconciler)
            .Handle(new SetResumeLanguageCommand(resume.Id.Value, "En"), ct);
        changed.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var review = await new ReviewResumeQueryHandler(
                db, _currentUser, _engine, _rubricProvider, Substitute.For<IFailedAccessLogger>(),
                TestFindingFingerprinter.Instance)
            .Handle(new ReviewResumeQuery(resume.Id.Value, "Ats"), ct);
        review.ShouldNotBeNull();
        // C7 now cites another word: the mark was about the Swedish finding, so it is not shown on
        // the English one, and that finding is open again.
        review.Verdicts.Single(v => v.CriterionId == "C7").UserStatus.ShouldBe("Open");
        // A1's finding is the same in both languages, so its mark stays.
        review.Verdicts.Single(v => v.CriterionId == "A1").UserStatus.ShouldBe("Resolved");

        var hub = await new GetResumesQueryHandler(db, _currentUser, _rubricProvider)
            .Handle(new GetResumesQuery(), ct);
        hub.Items.ShouldHaveSingleItem().OpenFindingCount.ShouldBe(1);
    }
}
