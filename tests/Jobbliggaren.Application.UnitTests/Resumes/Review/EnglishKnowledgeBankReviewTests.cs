using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #2341: an English CV is read with the English cliché and soft-skill list, chosen by the CV's
/// language, so A7 and A9 judge English text instead of passing it for want of Swedish words. Its
/// notes never ask its author for Swedish text.
/// </summary>
public class EnglishKnowledgeBankReviewTests
{
    private static readonly IReadOnlyList<string> NeutralBullets =
        ["Designed a test strategy for the payment service."];

    private static CvReviewEngine Engine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(), RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> VerdictAsync(string criterionId, string profile, ResumeLanguage language)
    {
        var parsed = Resume(
            profile: profile, experience: [Experience(bullets: NeutralBullets)], detectedLanguage: language);
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(parsed), RenderProfile.Ats, TestContext.Current.CancellationToken);
        return Verdict(result, criterionId);
    }

    private static string Note(CvCriterionVerdict verdict) =>
        verdict.Evidence.ShouldHaveSingleItem().ShouldBeOfType<TextSpanEvidence>().Note!;

    [Fact]
    public async Task A7_ShouldFindEnglishCliches_OnAnEnglishCv()
    {
        var a7 = await VerdictAsync(
            "A7", "A results-driven team player with a strong interest in test automation.", ResumeLanguage.En);

        a7.Verdict.ShouldBe(CriterionVerdict.Warn, "premise: two clichés, under the fail count of three");
        Note(a7).ShouldStartWith("klyscha:");
    }

    [Fact]
    public async Task A7_ShouldReadASwedishCv_WithTheSwedishListOnly()
    {
        var a7 = await VerdictAsync(
            "A7", "A results-driven team player with a strong interest in test automation.", ResumeLanguage.Sv);

        a7.Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task A9_ShouldFailEnglishSoftSkills_ThatNoSentenceBacksWithANumber()
    {
        var a9 = await VerdictAsync(
            "A9", "Reliable and detail-oriented QA engineer with a strong interest in test automation.", ResumeLanguage.En);

        a9.Verdict.ShouldBe(CriterionVerdict.Fail);
        Note(a9).ShouldBe("personlighetsadjektiv utan konkret exempel");
    }

    [Fact]
    public async Task A9_ShouldPassAnEnglishSoftSkill_ThatItsSentenceBacksWithANumber()
    {
        var a9 = await VerdictAsync(
            "A9", "Reliable QA engineer who cut the time to a verified fix from 9 to 3 days.", ResumeLanguage.En);

        a9.Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task A8_ShouldNotAskAnEnglishCvForASwedishSummary()
    {
        var a8 = await VerdictAsync(
            "A8", "Objective: To obtain a challenging position in software testing.", ResumeLanguage.En);

        a8.Verdict.ShouldBe(CriterionVerdict.Fail);
        Note(a8).ShouldBe("profiltext i \"Objective\"-USA-stil, skriv en kort sammanfattning");
    }

    [Fact]
    public async Task C4_ShouldNotCiteASwedishStandard_ToAnEnglishCv()
    {
        var c4 = await VerdictAsync("C4", "Experienced QA engineer. She led the test team in 2024.", ResumeLanguage.En);

        c4.Verdict.ShouldBe(CriterionVerdict.Warn);
        Note(c4).ShouldBe("tredje person: använd konsekvent perspektiv (CV-standard: utan pronomen)");
    }
}
