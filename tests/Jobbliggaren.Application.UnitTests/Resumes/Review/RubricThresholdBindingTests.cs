using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using NSubstitute;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Rubric v1.2 (Fas 4b PR-5, #654, CTO-bind D1) — DATA-BINDING behavioural guards for the
/// thresholds whose prose signal carries NO number (C2/C6/A9/B6), plus the fail-loud contract.
/// A prose↔data golden guard (the CvReviewEngineTests A1/A2/A4/A6/A7/A8/C3 kind) is impossible
/// where the prose has no numeral, so the ONLY honest closure is to prove the RULE reads the
/// DATA: mutate the target criterion's <see cref="RubricCriterion.Thresholds"/> in a substitute
/// <see cref="IRubricProvider"/> (everything else untouched, parity
/// <c>CvReviewEngineTests.FakeRubricProviderWithNullReasonOnA5</c>) and assert the verdict
/// BOUNDARY MOVES with the data. Were the rule reading a resurrected C# literal, the mutation
/// would not move the verdict — so a green test here is a green data-binding proof.
///
/// The engine is constructed directly (internal sealed, InternalsVisibleTo parity
/// CvReviewEngineTests); the cliché lexicon + verb mapper + analyzer are the REAL/deterministic
/// ports so only the mutated threshold can change a verdict. Threshold VALUES in the shipped
/// asset are never touched here — these tests mutate a SUBSTITUTE rubric only.
/// </summary>
public class RubricThresholdBindingTests
{
    private static CvReviewEngine EngineWith(IRubricProvider provider) =>
        new(provider, RealClicheLexicon(), RealVerbMapper(), Analyzer());

    // The REAL rubric with ONE criterion's Thresholds replaced — nothing else changes, so only the
    // mutated key can move a verdict (parity FakeRubricProviderWithNullReasonOnA5, CvReviewEngineTests).
    private static IRubricProvider ProviderWithThresholds(
        string criterionId, Dictionary<string, double> thresholds)
    {
        var real = RealRubric();
        var patched = real.Criteria
            .Select(c => c.Id == criterionId ? c with { Thresholds = thresholds } : c)
            .ToList();

        var provider = Substitute.For<IRubricProvider>();
        provider.GetRubric().Returns(real with { Criteria = patched });
        return provider;
    }

    private static async Task<CriterionVerdict> VerdictUnder(
        IRubricProvider provider, ParsedResume resume, string criterionId)
    {
        var result = await EngineWith(provider).ReviewAsync(
            CvReviewContext.FromParsed(resume), RenderProfile.Ats, TestContext.Current.CancellationToken);
        return Verdict(result, criterionId).Verdict;
    }

    // ===============================================================
    // C2 Ton — warnFromExclamationCount (prose carries no number)
    // ===============================================================

    [Fact]
    public async Task C2_ShouldMoveFromWarnToPass_WhenWarnFromExclamationCountIsRaised()
    {
        // A profile with EXACTLY 2 exclamation marks and no shouting run. Real rubric
        // warnFromExclamationCount = 2 → 2 ≥ 2 → Warn; raise it to 3 in the DATA → 2 < 3 → Pass.
        var resume = Resume(profile: "Jag levererade starka resultat! Jag ledde teamet mot målen!");

        (await VerdictUnder(RealRubricProvider(), resume, "C2"))
            .ShouldBe(CriterionVerdict.Warn, "2 utropstecken ≥ 2 (rubrikens värde) → Warn.");

        var raised = ProviderWithThresholds("C2",
            new() { [RubricThresholdKeys.WarnFromExclamationCount] = 3 });
        (await VerdictUnder(raised, resume, "C2"))
            .ShouldBe(CriterionVerdict.Pass, "Samma CV, tröskeln höjd till 3 → 2 < 3 → Pass (regeln läser DATA).");
    }

    // ===============================================================
    // C6 Förkortningar — maxUnexplainedAcronyms (prose carries no number)
    // ===============================================================

    [Fact]
    public async Task C6_ShouldMoveFromWarnToPass_WhenMaxUnexplainedAcronymsIsRaised()
    {
        // EXACTLY 3 distinct unexplained acronyms and no expansion "(". Real rubric
        // maxUnexplainedAcronyms = 2 → 3 > 2 → Warn; raise to 5 → 3 > 5 is false → Pass. The
        // experience is overridden to a clean entry so no stray acronym (e.g. "AB") inflates the count.
        var resume = Resume(
            profile: "Arbetade med API, SDK och CRM i flera leveranser.",
            experience:
            [
                Experience(title: "Utvecklare", organization: "Firman", period: "2021 – 2024",
                    bullets: ["Byggde och förbättrade interna flöden."]),
            ]);

        (await VerdictUnder(RealRubricProvider(), resume, "C6"))
            .ShouldBe(CriterionVerdict.Warn, "3 oförklarade förkortningar > 2 (rubrikens värde) → Warn.");

        var raised = ProviderWithThresholds("C6",
            new() { [RubricThresholdKeys.MaxUnexplainedAcronyms] = 5 });
        (await VerdictUnder(raised, resume, "C6"))
            .ShouldBe(CriterionVerdict.Pass, "Samma CV, tröskeln höjd till 5 → 3 ≤ 5 → Pass (regeln läser DATA).");
    }

    // ===============================================================
    // A9 Soft skills — failFromCount (prose carries no number)
    // ===============================================================

    [Fact]
    public async Task A9_ShouldMoveFromFailToWarn_WhenFailFromCountIsRaised()
    {
        // Two curated soft-skill adjectives, both UNBACKED (no measurable example in the same
        // sentence). Real rubric failFromCount = 2 → 2 unsupported ≥ 2 → Fail; raise to 3 → 2 < 3 →
        // Warn. Soft-skill phrases come from the REAL cliché lexicon (golden source), parity the
        // CvReviewEngineTests A9 cases.
        var soft = RealClicheLexicon().GetClicheList().Entries
            .Where(e => e.Kind == ClicheKind.SoftSkill)
            .Select(e => e.Phrase)
            .ToList();
        soft.Count.ShouldBeGreaterThanOrEqualTo(2, "assetet måste bära minst två soft-skill-fraser för testet.");

        var resume = Resume(profile: $"{soft[0]}. {soft[1]}.", experience: []);

        (await VerdictUnder(RealRubricProvider(), resume, "A9"))
            .ShouldBe(CriterionVerdict.Fail, "2 obestyrkta adjektiv ≥ 2 (rubrikens värde) → Fail.");

        var raised = ProviderWithThresholds("A9",
            new() { [RubricThresholdKeys.FailFromCount] = 3 });
        (await VerdictUnder(raised, resume, "A9"))
            .ShouldBe(CriterionVerdict.Warn, "Samma CV, tröskeln höjd till 3 → 2 < 3 → Warn (regeln läser DATA).");
    }

    // ===============================================================
    // B6 Datumformat — maxDistinctDateFormats (prose carries no number)
    // ===============================================================

    [Fact]
    public async Task B6_ShouldMoveFromWarnToPass_WhenMaxDistinctDateFormatsIsRaised()
    {
        // Two experiences with DISTINCT parseable date-format tokens: "YYYY" (year granularity) and
        // "MM/YYYY" (month granularity) → 2 distinct formats. Real rubric maxDistinctDateFormats = 1
        // → 2 > 1 → Warn; raise to 2 → 2 ≤ 2 → Pass.
        var resume = Resume(experience:
        [
            Experience(period: "2018 – 2020", rawText: "Roll A 2018 – 2020"),
            Experience(period: "01/2019 – 05/2020", rawText: "Roll B 01/2019 – 05/2020"),
        ]);

        (await VerdictUnder(RealRubricProvider(), resume, "B6"))
            .ShouldBe(CriterionVerdict.Warn, "2 olika datumformat > 1 (rubrikens värde) → Warn.");

        var raised = ProviderWithThresholds("B6",
            new() { [RubricThresholdKeys.MaxDistinctDateFormats] = 2 });
        (await VerdictUnder(raised, resume, "B6"))
            .ShouldBe(CriterionVerdict.Pass, "Samma CV, tröskeln höjd till 2 → 2 ≤ 2 → Pass (regeln läser DATA).");
    }

    // ===============================================================
    // Fail-loud: a required key absent from the DATA throws (no literal fallback)
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldThrowNamingTheMissingKey_WhenARequiredThresholdIsAbsent()
    {
        // CTO-bind D1 mitigation 2: remove A8's required maxWords from the DATA and the rule's
        // RequiredThreshold(maxWords) throws — NO hardcoded-literal fallback (a fallback would
        // resurrect the very literal the move removes). The exception NAMES the missing key so an
        // asset↔rule drift fails the review loudly, never a silent wrong verdict.
        var engine = EngineWith(ProviderWithThresholds("A8", new())); // A8 with an EMPTY thresholds dict

        // A default CV has a non-empty profile, so A8's rule reaches its RequiredThreshold(maxWords) line.
        var act = async () => await engine.ReviewAsync(
            CvReviewContext.FromParsed(Resume()), RenderProfile.Ats, TestContext.Current.CancellationToken);

        var ex = await act.ShouldThrowAsync<InvalidOperationException>();
        // The message must name the missing key (fail-loud, no literal-fallback) and the drifting
        // criterion — so an asset↔rule drift fails the review loudly, never a silent wrong verdict.
        ex.Message.ShouldContain(RubricThresholdKeys.MaxWords);
        ex.Message.ShouldContain("A8");
    }
}
