using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #2352: A6 reads a bullet's first word as its first token that carries a letter or a digit, so a
/// lead glyph never turns the capitalised first real word into a "named system". Driven through the
/// real engine on both arms.
/// </summary>
public class A6ConcretionRuleTests
{
    // No measurable digit and no capitalised word past the first, so each bullet is vague.
    private static readonly string[] VagueBullets =
    [
        "Mentored two junior testers.",
        "Wrote manual test cases for the portal.",
        "Followed up defects with the developers.",
    ];

    // The RawText the real extractor produced for two synthetic PDFs with the same text, rendered by
    // headless Chrome (11 pt Arial, A4) and measured 2026-11-26 by the #2342 session's probe:
    // cv-en-ul.pdf (a <ul> list; Chrome puts no glyph in the text layer) and cv-en-glyph.pdf (a
    // literal "• " typed at each item).
    private static readonly string MeasuredEnglishCv = string.Join('\n',
    [
        "Testa Testsson",
        "testa.testsson@example.com | 070-000 00 00 | Exempelstad",
        "Profile",
        "Quality assurance engineer with a background in automated testing of web applications and a",
        "strong interest in continuous delivery and test design for distributed systems.",
        "Experience",
        "QA Engineer — Exempelbolaget AB",
        "2021 – 2024",
        "Automated 35 regression tests with Selenium and C#, reducing the regression cycle from two",
        "days to four hours.",
        "Designed a test strategy for the payment service together with three development teams and",
        "the product owner group.",
        "Introduced contract tests between the order service and the warehouse service, which removed",
        "a class of release blockers.",
        "Mentored two junior testers.",
        "Test Analyst — Fiktiva Konsult AB",
        "2018 – 2021",
        "Wrote and maintained manual test cases for an insurance portal used by customers of a",
        "regional insurance company.",
        "Reported defects in Jira and followed them up with the developers until they were verified and",
        "closed.",
        "Education",
        "BSc Computer Science — Exempeluniversitetet",
        "2015 – 2018",
        "Skills",
        "Selenium, C#, Playwright, SQL, Jira",
        "Languages",
        "Swedish, English",
    ]);

    private static readonly string MeasuredEnglishGlyphCv = string.Join('\n',
    [
        "Testa Testsson",
        "testa.testsson@example.com | 070-000 00 00 | Exempelstad",
        "Profile",
        "Quality assurance engineer with a background in automated testing of web applications and a",
        "strong interest in continuous delivery and test design for distributed systems.",
        "Experience",
        "QA Engineer — Exempelbolaget AB",
        "2021 – 2024",
        "• Automated 35 regression tests with Selenium and C#, reducing the regression cycle from two",
        "days to four hours.",
        "• Designed a test strategy for the payment service together with three development teams and the",
        "product owner group.",
        "• Introduced contract tests between the order service and the warehouse service, which removed a",
        "class of release blockers.",
        "• Mentored two junior testers.",
        "Test Analyst — Fiktiva Konsult AB",
        "2018 – 2021",
        "• Wrote and maintained manual test cases for an insurance portal used by customers of a regional",
        "insurance company.",
        "• Reported defects in Jira and followed them up with the developers until they were verified and",
        "closed.",
        "Education",
        "BSc Computer Science — Exempeluniversitetet",
        "2015 – 2018",
        "Skills",
        "Selenium, C#, Playwright, SQL, Jira",
        "Languages",
        "Swedish, English",
    ]);

    private static CvReviewEngine Engine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(), RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> A6Async(CvReviewContext context) =>
        Verdict(await Engine().ReviewAsync(context, RenderProfile.Ats, TestContext.Current.CancellationToken), "A6");

    private static Task<CvCriterionVerdict> A6FromParsedAsync(IReadOnlyList<string> bullets) =>
        A6Async(CvReviewContext.FromParsed(Resume(experience: [Experience(bullets: bullets)])));

    private static Task<CvCriterionVerdict> A6OnTheCanonicalArmAsync(IEnumerable<string> descriptionLines)
    {
        var content = new ResumeContent(
            new PersonalInfo("Testa Testsson", "testa.testsson@example.com", "070-000 00 00", "Exempelstad"),
            experiences:
            [
                new Experience("Exempelbolaget AB", "QA Engineer",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 1, 1), string.Join('\n', descriptionLines)),
            ]);
        return A6Async(CvReviewContext.FromCanonical(content, ResumeContentLinearizer.Linearize(content), ResumeLanguage.En));
    }

    [Theory]
    [InlineData("• ")]
    [InlineData("● ")]
    [InlineData("▪ ")]
    [InlineData("- ")]
    [InlineData("* ")]
    [InlineData(" ")] // Word's Symbol-font bullet; in neither marker set
    [InlineData("➤ ")] // in neither marker set
    public async Task A6_ShouldJudgeTheSameBulletsAlike_WithAndWithoutALeadGlyph(string glyph)
    {
        var plain = await A6FromParsedAsync(VagueBullets);
        var led = await A6FromParsedAsync(VagueBullets.Select(b => glyph + b).ToArray());

        plain.Verdict.ShouldBe(CriterionVerdict.Fail, "premise: every bullet is vague");
        led.Verdict.ShouldBe(plain.Verdict);
    }

    [Fact]
    public async Task A6_ShouldNotCountTheFirstRealWordOfAGlyphLedBullet_AsANamedSystem()
    {
        var a6 = await A6FromParsedAsync(["• Mentored two junior testers."]);

        a6.Verdict.ShouldBe(CriterionVerdict.Fail);
    }

    [Fact]
    public async Task A6_ShouldStillCountACapitalisedWordAfterTheFirstRealWord_AsANamedSystem()
    {
        var a6 = await A6FromParsedAsync(["• Automated the regression suite in Selenium."]);

        a6.Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    [Theory]
    [InlineData("- ")]
    [InlineData("• ")]
    public async Task A6_ShouldJudgeTheCanonicalArmAlike_WithAndWithoutUserTypedMarkers(string marker)
    {
        var plain = await A6OnTheCanonicalArmAsync(VagueBullets);
        var marked = await A6OnTheCanonicalArmAsync(VagueBullets.Select(b => marker + b));

        plain.Verdict.ShouldBe(CriterionVerdict.Fail, "premise: every bullet is vague");
        marked.Verdict.ShouldBe(plain.Verdict);
    }

    [Fact]
    public async Task A6_ShouldGiveTheMeasuredGlyphAndGlyphlessPdfsTheSameVerdict()
    {
        var glyphless = await A6Async(CvReviewContext.FromParsed(ResumeFromCvText(MeasuredEnglishCv)));
        var glyphLed = await A6Async(CvReviewContext.FromParsed(ResumeFromCvText(MeasuredEnglishGlyphCv)));

        glyphless.Verdict.ShouldBe(CriterionVerdict.Fail, "premise: most measured bullets are vague");
        glyphLed.Verdict.ShouldBe(glyphless.Verdict);
    }
}
