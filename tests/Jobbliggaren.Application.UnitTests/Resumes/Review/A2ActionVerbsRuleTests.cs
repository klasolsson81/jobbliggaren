using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// A2 reads a bullet's opener with the production analyzer (<see cref="LocalTextAnalyzer"/> over
/// <see cref="SnowballStemmer"/>), because the whitespace stub the other review tests use neither
/// drops stopwords nor stems, so it cannot see a weak phrase collapse onto a strong verb. The opener
/// is the bullet from its first word (#2352); the knowledge bank's weak phrase is read before its
/// strong verbs.
/// </summary>
public class A2ActionVerbsRuleTests
{
    private const string WeakNote = "inleds med ett svagt verb (se verb-mappningen)";

    private static CvReviewEngine Engine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), new LocalTextAnalyzer(new SnowballStemmer()),
            AllCorrectSpellChecker(), RealAllowlist(), RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> A2Async(CvReviewContext context) =>
        Verdict(await Engine().ReviewAsync(context, RenderProfile.Ats, TestContext.Current.CancellationToken), "A2");

    private static Task<CvCriterionVerdict> A2FromParsedAsync(params string[] bullets) =>
        A2Async(CvReviewContext.FromParsed(Resume(experience: [Experience(bullets: bullets)])));

    private static TextSpanEvidence Evidence(CvCriterionVerdict verdict) =>
        verdict.Evidence.ShouldHaveSingleItem().ShouldBeOfType<TextSpanEvidence>();

    private static string Capitalised(string phrase) => char.ToUpperInvariant(phrase[0]) + phrase[1..];

    public static TheoryData<string> WeakPhrases() => [.. RealVerbMapper().GetVerbMapping().WeakVerbs.Select(w => w.Weak)];

    public static TheoryData<string> StrongVerbs() =>
        [.. RealVerbMapper().GetVerbMapping().StrongVerbGroups.SelectMany(g => g.Verbs)];

    // Measured 2026-11-26 with the production analyzer: "Var ansvarig för budgeten" lexemises to
    // [ansvar, budget], the stem "ansvarade för" also lexemises to.
    [Theory]
    [InlineData("")]
    [InlineData("• ")]
    [InlineData("- ")]
    public async Task A2_ShouldReadTheBanksOwnWeakPhrase_AsWeak_NeverAsStrong(string glyph)
    {
        var a2 = await A2FromParsedAsync(
            glyph + "Var ansvarig för budgeten.", glyph + "Ledde teamet om åtta personer.", glyph + "Ledde inköpen.");

        a2.Verdict.ShouldBe(CriterionVerdict.Fail);
        Evidence(a2).Note.ShouldBe(WeakNote);
        Evidence(a2).Span.Quote.ShouldBe(glyph + "Var ansvarig för budgeten.");
    }

    [Theory]
    [MemberData(nameof(WeakPhrases))]
    public async Task A2_ShouldReadEveryWeakPhraseInTheBank_AsAWeakOpener(string weak)
    {
        var a2 = await A2FromParsedAsync($"{Capitalised(weak)} arbetet på avdelningen.");

        a2.Verdict.ShouldBe(CriterionVerdict.Fail);
        Evidence(a2).Note.ShouldBe(WeakNote);
    }

    [Theory]
    [MemberData(nameof(StrongVerbs))]
    public async Task A2_ShouldReadEveryStrongVerbInTheBank_AsAStrongOpener(string verb)
    {
        var a2 = await A2FromParsedAsync($"{Capitalised(verb)} arbetet på avdelningen.");

        a2.Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task A2_ShouldReadAMarkerLedWeakOpener_AsWeak_OnTheCanonicalArm()
    {
        var content = new ResumeContent(
            new PersonalInfo("Testa Testsson", "testa.testsson@example.com", "070-000 00 00", "Exempelstad"),
            experiences:
            [
                new Experience("Exempelbutiken AB", "Säljare",
                    new DateOnly(2020, 1, 1), new DateOnly(2024, 1, 1), "- Hade hand om kassan.\n- Ledde inköpen."),
            ]);

        var a2 = await A2Async(CvReviewContext.FromCanonical(content, ResumeContentLinearizer.Linearize(content), ResumeLanguage.Sv));

        a2.Verdict.ShouldBe(CriterionVerdict.Fail);
        Evidence(a2).Note.ShouldBe(WeakNote);
    }
}
