using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// C3 divides passives by the sentences of the CV's prose. On the staging arm that prose used to
/// hold each entry's header and period lines, which counted as sentences and thinned the ratio;
/// the canonical arm never had them. Both arms now read the profile and the description lines.
/// </summary>
public class C3ActiveVoiceRuleTests
{
    // One passive ("infördes") in three description sentences.
    private static readonly string[] Bullets =
    [
        "Rutinen infördes av chefen.",
        "Ledde teamet om åtta personer.",
        "Byggde tjänsten för betalningar.",
    ];

    private static CvReviewEngine Engine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(), RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> C3Async(CvReviewContext context) =>
        Verdict(await Engine().ReviewAsync(context, RenderProfile.Ats, TestContext.Current.CancellationToken), "C3");

    [Fact]
    public async Task C3_ShouldCountTheSameSentencesOnBothArms_LeavingHeaderAndPeriodLinesOut()
    {
        var staging = await C3Async(CvReviewContext.FromParsed(
            Resume(profile: null, experience: [Experience(bullets: Bullets)])));

        var content = new ResumeContent(
            new PersonalInfo("Testa Testsson", "testa.testsson@example.com", "070-000 00 00", "Exempelstad"),
            experiences:
            [
                new Experience("Acme AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 1, 1), string.Join('\n', Bullets)),
            ]);
        var canonical = await C3Async(CvReviewContext.FromCanonical(
            content, ResumeContentLinearizer.Linearize(content), ResumeLanguage.Sv));

        canonical.Verdict.ShouldBe(CriterionVerdict.Fail, "premise: 1 passive in 3 sentences is over the fail ratio");
        staging.Verdict.ShouldBe(canonical.Verdict);
    }
}
