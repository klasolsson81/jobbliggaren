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
        A2FromParsedAsync(ResumeLanguage.Sv, bullets);

    private static Task<CvCriterionVerdict> A2FromParsedAsync(ResumeLanguage language, params string[] bullets) =>
        A2Async(CvReviewContext.FromParsed(
            Resume(experience: [Experience(bullets: bullets)], detectedLanguage: language)));

    // The rest of a bullet after its opener, in the CV's language.
    private static string Object(ResumeLanguage language) =>
        language == ResumeLanguage.En ? "the work of the department." : "arbetet på avdelningen.";

    private static TextSpanEvidence Evidence(CvCriterionVerdict verdict) =>
        verdict.Evidence.ShouldHaveSingleItem().ShouldBeOfType<TextSpanEvidence>();

    private static string Capitalised(string phrase) => char.ToUpperInvariant(phrase[0]) + phrase[1..];

    public static TheoryData<string, string> WeakPhrases()
    {
        var data = new TheoryData<string, string>();
        foreach (var language in ResumeLanguage.List)
        {
            foreach (var weak in RealVerbMapper().GetVerbMapping(language).WeakVerbs)
            {
                data.Add(language.Name, weak.Weak);
            }
        }

        return data;
    }

    public static TheoryData<string, string> StrongVerbs()
    {
        var data = new TheoryData<string, string>();
        foreach (var language in ResumeLanguage.List)
        {
            foreach (var verb in RealVerbMapper().GetVerbMapping(language).StrongVerbGroups.SelectMany(g => g.Verbs))
            {
                data.Add(language.Name, verb);
            }
        }

        return data;
    }

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

    // A stopword opener has no lexeme of its own, so it is never strong, even when a later word
    // carries a strong verb's stem: "ansvarig" stems to "ansvar", the stem of the strong
    // "ansvarade för". The English list matches listed forms, and its row holds the same.
    [Theory]
    [InlineData("Sv", "Som ansvarig för budgeten minskade jag kostnaderna.", "Ledde teamet om åtta personer.", "Ledde inköpen.")]
    [InlineData("En", "As manager of the budget, cut costs by a tenth.", "Led the team of eight people.", "Led the purchasing.")]
    public async Task A2_ShouldNeverReadAStopwordOpener_AsStrong(
        string language, string stopwordLed, string strong, string alsoStrong)
    {
        var a2 = await A2FromParsedAsync(ResumeLanguage.FromName(language), stopwordLed, strong, alsoStrong);

        a2.Verdict.ShouldBe(CriterionVerdict.Warn, "premise: two strong openers in three, between the two ratios");
        Evidence(a2).Note.ShouldBe("inleds inte med ett starkt handlingsverb");
        Evidence(a2).Span.Quote.ShouldBe(stopwordLed);
    }

    // ADR 0191 D7: the English list matches its listed forms, so a derived noun at the opener is not
    // a strong verb, though the stemmer reads it as one ("development" as "develop").
    [Theory]
    [InlineData("Development of an internal reporting tool.")]
    [InlineData("Management of the release calendar.")]
    [InlineData("Manager of the support desk for two years.")]
    [InlineData("Developer in the payments team.")]
    [InlineData("Implementation of a new test framework.")]
    public async Task A2_ShouldNeverReadAnEnglishDerivedNoun_AsAStrongOpener(string nounLed)
    {
        var a2 = await A2FromParsedAsync(
            ResumeLanguage.En, nounLed, "Led the team of eight people.", "Led the purchasing.");

        a2.Verdict.ShouldBe(CriterionVerdict.Warn, "premise: two strong openers in three, between the two ratios");
        Evidence(a2).Note.ShouldBe("inleds inte med ett starkt handlingsverb");
        Evidence(a2).Span.Quote.ShouldBe(nounLed);
    }

    // Accepted and known (ADR 0191 D7): an inflected form that is also a noun reads as its verb, and
    // so does a Swedish agent noun whose stem is a strong verb's. A pronoun opener is never strong.
    [Theory]
    [InlineData("En", "Test Analyst at Fiktiva Konsult AB.", true)]
    [InlineData("En", "Testing of the checkout flow for the web shop.", true)]
    [InlineData("En", "Design of the test framework.", true)]
    [InlineData("En", "Lead developer for the payment service.", true)]
    [InlineData("Sv", "Utvecklare i teamet för betalningar.", true)]
    [InlineData("Sv", "Jag ledde teamet om åtta personer.", false)]
    [InlineData("En", "I led the team of eight people.", false)]
    public async Task A2_ShouldReadTheAcceptedOpeners_AsAdr0191RecordsThem(string language, string bullet, bool strong)
    {
        var a2 = await A2FromParsedAsync(ResumeLanguage.FromName(language), bullet);

        a2.Verdict.ShouldBe(strong ? CriterionVerdict.Pass : CriterionVerdict.Fail);
    }

    // A number is a bullet's first word, so a number-led bullet does not open with a verb, even when
    // its next word is spelled like a bank verb: "automatiserade" and "automated" are bank entries,
    // and here they are participles. The year row is accepted and known (ADR 0191 D7): no rule
    // tells a year from a quantity by its digits, so "2021 Ledde …" opens with its year.
    [Theory]
    [InlineData("Sv", "35 automatiserade regressionstester per release.", "Ledde teamet om åtta personer.", "Ledde inköpen.")]
    [InlineData("En", "35 automated regression tests per release.", "Led the team of eight people.", "Led the purchasing.")]
    [InlineData("Sv", "2021 Ledde teamet om åtta personer.", "Ledde inköpen.", "Ledde flytten till nya lokaler.")]
    public async Task A2_ShouldReadANumberLedBullet_AsOpeningWithItsNumber(
        string language, string numberLed, string strong, string alsoStrong)
    {
        var a2 = await A2FromParsedAsync(ResumeLanguage.FromName(language), numberLed, strong, alsoStrong);

        a2.Verdict.ShouldBe(CriterionVerdict.Warn, "premise: two strong openers in three, between the two ratios");
        Evidence(a2).Note.ShouldBe("inleds inte med ett starkt handlingsverb");
        Evidence(a2).Span.Quote.ShouldBe(numberLed);
    }

    [Theory]
    [MemberData(nameof(WeakPhrases))]
    public async Task A2_ShouldReadEveryWeakPhraseInTheBank_AsAWeakOpener(string language, string weak)
    {
        var cvLanguage = ResumeLanguage.FromName(language);
        var a2 = await A2FromParsedAsync(cvLanguage, $"{Capitalised(weak)} {Object(cvLanguage)}");

        a2.Verdict.ShouldBe(CriterionVerdict.Fail);
        Evidence(a2).Note.ShouldBe(WeakNote);
    }

    [Theory]
    [MemberData(nameof(StrongVerbs))]
    public async Task A2_ShouldReadEveryStrongVerbInTheBank_AsAStrongOpener(string language, string verb)
    {
        var cvLanguage = ResumeLanguage.FromName(language);
        var a2 = await A2FromParsedAsync(cvLanguage, $"{Capitalised(verb)} {Object(cvLanguage)}");

        a2.Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    // #2341: measured 2026-11-26, an English CV whose bullets open with "Designed", "Led" or
    // "Automated" failed A2, because the strong verbs were Swedish only.
    [Fact]
    public async Task A2_ShouldPassAnEnglishCv_WhoseBulletsOpenWithStrongEnglishVerbs()
    {
        var a2 = await A2FromParsedAsync(ResumeLanguage.En,
            "Designed a test strategy for the payment service.",
            "Led the migration of the end-to-end suite to a new framework.",
            "Automated 35 regression tests with Selenium and C#.");

        a2.Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task A2_ShouldFlagAnEnglishWeakOpener()
    {
        var a2 = await A2FromParsedAsync(ResumeLanguage.En,
            "Responsible for manual test cases for an insurance portal.",
            "Designed a test strategy for the payment service.",
            "Led the migration of the end-to-end suite to a new framework.");

        a2.Verdict.ShouldBe(CriterionVerdict.Fail);
        Evidence(a2).Note.ShouldBe(WeakNote);
        Evidence(a2).Span.Quote.ShouldBe("Responsible for manual test cases for an insurance portal.");
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
