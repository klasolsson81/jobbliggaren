using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using NSubstitute;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-6a (#655, ADR 0093 §D4, CTO-bind PR-6 D-C) — C7 "Stavning (maskinell kontroll)",
/// the WARN-posture machine spell-check criterion. Driven through the REAL <see cref="CvReviewEngine"/>
/// wired to the REAL rubric/cliché/verb + allowlist but a CONTROLLABLE stub
/// <see cref="ISpellChecker"/> that rejects ONLY the tokens a test names (every other word is
/// deemed correct), so the assertions stay on C7's RULE logic (tokenize → skip-rules → threshold
/// → verdict) and never on DSSO/en_US vocabulary. The threshold (warnFromMisspellingCount = 2) is
/// read from the REAL rubric DATA, not hardcoded here.
///
/// Coverage (each success + each failure/edge, CLAUDE.md §7): Warn at/above threshold, Pass below
/// and at zero, allowlist suppression, the proper-noun (capitalised) ceiling, acronym/internal-caps/
/// short-token structural skips, language dispatch, never-Fail (WARN-posture), and PII-safe evidence.
/// </summary>
public class C7SpellingRuleTests
{
    // The rule reads warnFromMisspellingCount from the shipped rubric DATA (fail-loud). The tests
    // assert relative to it so a future value change never silently breaks them.
    private static double WarnFrom() =>
        RealRubric().Criteria.Single(c => c.Id == "C7")
            .RequiredThreshold(RubricThresholdKeys.WarnFromMisspellingCount);

    private static CvReviewEngine EngineWith(ISpellChecker checker) =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(), checker, RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

    // A stub that deems EVERY word correct except the named tokens (matched by NFC-identity for the
    // ASCII test vocabulary). C7 only ever hands it a lowercase-initial, non-allowlisted, checkable token.
    private static ISpellChecker CheckerRejecting(params string[] misspelled)
    {
        var checker = Substitute.For<ISpellChecker>();
        checker.Check(Arg.Any<string>(), Arg.Any<TextLanguage>()).Returns(true);
        foreach (var word in misspelled)
        {
            checker.Check(word, Arg.Any<TextLanguage>()).Returns(false);
        }

        return checker;
    }

    private static async Task<CvCriterionVerdict> C7Async(ISpellChecker checker, ParsedResume resume)
    {
        var result = await EngineWith(checker).ReviewAsync(
            CvReviewContext.FromParsed(resume), RenderProfile.Ats, TestContext.Current.CancellationToken);
        return Verdict(result, "C7");
    }

    // ===============================================================
    // (a) 2+ distinct misspellings → Warn, quotes the first, note carries the count, never Fail
    // ===============================================================

    [Fact]
    public async Task C7_ShouldWarnCitingTheFirstMisspelling_WhenDistinctCountReachesTheThreshold()
    {
        // Exactly 2 distinct lowercase misspellings → 2 >= warnFrom(2) → Warn.
        var resume = Resume(profile: "Jag har erfarenhet av stavfle och felstav i arbetet.");
        var checker = CheckerRejecting("stavfle", "felstav");

        var c7 = await C7Async(checker, resume);

        c7.Verdict.ShouldBe(CriterionVerdict.Warn);
        var span = c7.Evidence.ShouldHaveSingleItem().ShouldBeOfType<TextSpanEvidence>();
        span.Span.Quote.ShouldBe("stavfle", "C7 quotes the FIRST distinct misspelling in prose order.");
        span.Note.ShouldNotBeNull();
        // The note carries the distinct-misspelling count ("2 möjliga stavfel ...").
        span.Note!.ShouldContain("2");
    }

    [Fact]
    public async Task C7_ShouldStayWarnAndNeverFail_WhenManyMisspellingsAreFound()
    {
        // WARN-posture (CTO-bind PR-6 D-C / D4): C7 is a soft "check the spelling" nudge — it NEVER
        // takes the critical Fail slot no matter how many suspected typos there are (C1 keeps it).
        var resume = Resume(
            profile: "Jag har alfaa betaa gammaa deltaa epsiloo i arbetet.");
        var checker = CheckerRejecting("alfaa", "betaa", "gammaa", "deltaa", "epsiloo");

        var c7 = await C7Async(checker, resume);

        c7.Verdict.ShouldBe(CriterionVerdict.Warn);
        c7.Verdict.ShouldNotBe(CriterionVerdict.Fail, "C7 is WARN-posture — a misspelling is never a Fail.");
    }

    // ===============================================================
    // (b)/(c) Below threshold and zero → Pass (structural evidence)
    // ===============================================================

    [Fact]
    public async Task C7_ShouldPass_WhenOnlyOneMisspellingIsBelowTheThreshold()
    {
        WarnFrom().ShouldBe(2, "the fixture assumes the shipped warn-threshold is 2.");
        var resume = Resume(profile: "Jag har erfarenhet av stavfle i arbetet.");
        var checker = CheckerRejecting("stavfle");

        var c7 = await C7Async(checker, resume);

        c7.Verdict.ShouldBe(CriterionVerdict.Pass, "1 misspelling < 2 → Pass (below the warn-threshold).");
        c7.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>();
    }

    [Fact]
    public async Task C7_ShouldPass_WhenNoTokenIsMisspelled()
    {
        var resume = Resume(profile: "Jag levererade goda resultat tillsammans med teamet.");
        var checker = CheckerRejecting(); // every word correct

        var c7 = await C7Async(checker, resume);

        c7.Verdict.ShouldBe(CriterionVerdict.Pass);
        c7.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>();
    }

    // ===============================================================
    // (d) Allowlisted terms are suppressed even when the checker rejects them
    // ===============================================================

    [Fact]
    public async Task C7_ShouldSuppressAllowlistedTerms_EvenWhenTheCheckerRejectsThem()
    {
        // "kubernetes" and "docker" are committed allowlist terms. Were they NOT suppressed, 2 rejects
        // would reach the threshold → Warn; because the allowlist short-circuits BEFORE the checker,
        // the count is 0 → Pass, and the checker is never even consulted for them.
        var resume = Resume(profile: "Jag jobbade med kubernetes och docker dagligen.");
        var checker = CheckerRejecting("kubernetes", "docker");

        var c7 = await C7Async(checker, resume);

        c7.Verdict.ShouldBe(CriterionVerdict.Pass, "allowlisted proper nouns/tech terms are never flagged.");
        checker.DidNotReceive().Check("kubernetes", Arg.Any<TextLanguage>());
        checker.DidNotReceive().Check("docker", Arg.Any<TextLanguage>());
    }

    // ===============================================================
    // (e) Capitalised tokens are skipped — the honest proper-noun ceiling
    // ===============================================================

    [Fact]
    public async Task C7_ShouldSkipCapitalisedTokens_EvenWhenTheCheckerRejectsThem()
    {
        // A capitalised token is a proper noun / place / company name / sentence opener the
        // determinism cannot verify — never flagged as a misspelling (PII-adjacent, wrong). The rule
        // never even calls the checker for it.
        var resume = Resume(profile: "Vi samarbetade med Kompanjonix och Leverantörex under året.");
        var checker = CheckerRejecting("Kompanjonix", "Leverantörex");

        var c7 = await C7Async(checker, resume);

        c7.Verdict.ShouldBe(CriterionVerdict.Pass, "capitalised tokens are the proper-noun ceiling — skipped.");
        checker.DidNotReceive().Check("Kompanjonix", Arg.Any<TextLanguage>());
        checker.DidNotReceive().Check("Leverantörex", Arg.Any<TextLanguage>());
    }

    // ===============================================================
    // (f) Acronyms, internal-caps tech tokens, and 1-char tokens are structural skips
    // ===============================================================

    [Fact]
    public async Task C7_ShouldSkipAcronymsInternalCapsAndOneCharTokens_EvenWhenRejected()
    {
        // "API" (ALLCAPS acronym → first char not lowercase), "iOS" (internal uppercase = camelCase
        // tech token), "e" (1-char) are all structural skips (ADR 0093 §D3 detection SHAPE). None is
        // ever spell-checked, so a rejecting checker can't turn any into a misspelling → Pass.
        var resume = Resume(profile: "Jag använde API och iOS i e uppdrag.");
        var checker = CheckerRejecting("API", "iOS", "e");

        var c7 = await C7Async(checker, resume);

        c7.Verdict.ShouldBe(CriterionVerdict.Pass);
        checker.DidNotReceive().Check("API", Arg.Any<TextLanguage>());
        checker.DidNotReceive().Check("iOS", Arg.Any<TextLanguage>());
        checker.DidNotReceive().Check("e", Arg.Any<TextLanguage>());
    }

    // ===============================================================
    // (g) Language dispatch — the token is checked against the CV's TextLanguage
    // ===============================================================

    [Fact]
    public async Task C7_ShouldCheckAgainstSwedish_WhenTheCvIsSwedish()
    {
        var resume = Resume(
            profile: "Jag hade ansvar för leveranser.", detectedLanguage: ResumeLanguage.Sv);
        var checker = CheckerRejecting(); // all correct — we assert the language, not the verdict

        _ = await C7Async(checker, resume);

        checker.Received().Check(Arg.Any<string>(), TextLanguage.Swedish);
        checker.DidNotReceive().Check(Arg.Any<string>(), TextLanguage.English);
    }

    [Fact]
    public async Task C7_ShouldCheckAgainstEnglish_WhenTheCvIsEnglish()
    {
        var resume = Resume(
            profile: "I delivered several releases.", detectedLanguage: ResumeLanguage.En);
        var checker = CheckerRejecting();

        _ = await C7Async(checker, resume);

        checker.Received().Check(Arg.Any<string>(), TextLanguage.English);
        checker.DidNotReceive().Check(Arg.Any<string>(), TextLanguage.Swedish);
    }

    // ===============================================================
    // (h) Evidence is structurally PII-safe — C7 cites a letter-only word token, never digits
    // ===============================================================

    [Fact]
    public async Task C7_ShouldCiteALetterOnlyWordToken_NeverADigitSequence()
    {
        // C7's tokenizer is letter-only (\p{L}+...), so a personnummer (digits) can NEVER be the
        // cited quote — the evidence is PII-safe BY CONSTRUCTION, not by after-the-fact redaction.
        var resume = Resume(
            profile: "Under 2024 hade jag ansvar för stavfle och felstav i arbetet.");
        var checker = CheckerRejecting("stavfle", "felstav");

        var c7 = await C7Async(checker, resume);

        c7.Verdict.ShouldBe(CriterionVerdict.Warn);
        var quote = c7.Evidence.ShouldHaveSingleItem().ShouldBeOfType<TextSpanEvidence>().Span.Quote;
        quote.ShouldNotBeNullOrEmpty();
        quote.ShouldAllBe(ch => char.IsLetter(ch), "the cited token is letters only — never a digit run.");
    }
}
