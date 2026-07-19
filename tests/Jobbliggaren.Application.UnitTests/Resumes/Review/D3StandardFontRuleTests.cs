using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b #891 (ADR 0108) — D3 "Standardtypsnitt", the body-font criterion driven off the
/// font runs the <c>ICvLayoutAnalyzer</c> collected at import. Driven through the REAL
/// <see cref="CvReviewEngine"/> (all-correct spell-checker stub so C7 never interferes). D3 is
/// an AtsOnly criterion → assessed in the ATS profile, absent from a Visual review. Semantics:
///   • no font runs (null layout / null runs / empty runs / DOCX / failed / canonical) → NotAssessed;
///   • body = the modal point size by letter count; dominant family = the heaviest run at that size;
///   • Fail = the modal body font is an ICON/DINGBAT typeface (IconFontClassifier, #957) — the
///     criterion's only Fail; a non-modal icon run never triggers it (D4's permitted territory);
///   • Pass = allowlisted family (EXACT-equality after normalisation) AND modal pt ≥ the floor;
///   • Warn = otherwise (family not allowlisted, and/or below the floor) — terminal for family/size (#957).
/// The pt floor (fontBodyPtWarnBelow) and the allowlist are versioned DATA, derived here from the
/// real assets (anti-stale, parity <c>E2WhitespaceRuleTests.Floor()</c>) — never a literal in a decision.
/// </summary>
public class D3StandardFontRuleTests
{
    private static CvReviewEngine Engine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> D3Async(CvLayoutMetrics? layout)
    {
        // D3 is AtsOnly — it is only emitted in the ATS profile.
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(Resume(layoutMetrics: layout)),
            RenderProfile.Ats, TestContext.Current.CancellationToken);
        return Verdict(result, "D3");
    }

    // The readable-body pt floor as versioned DATA (rubric v2.2 fontBodyPtWarnBelow); never a
    // literal in the test. RequiredThreshold returns a double (10.0) — cast to the integer pt the
    // font runs carry.
    private static int Floor() =>
        (int)RealRubric().Criteria.Single(c => c.Id == "D3")
            .RequiredThreshold(RubricThresholdKeys.FontBodyPtWarnBelow);

    private static CvLayoutMetrics WithRuns(params CvFontRun[] runs) =>
        CvLayoutMetrics.Analyzed(fileSizeBytes: 200_000, pageCount: 1, minMarginPoints: 40, fontRuns: runs);

    // A minimal, valid canonical CV — the app-managed arm has no source-file geometry, so its
    // Layout is null (the canonical condition D3 keys off).
    private static ResumeContent CanonicalContent() => new(
        new PersonalInfo("Anna Andersson", "anna.andersson@example.com", "070-123 45 67", "Stockholm"),
        experiences:
        [
            new Experience("Acme AB", "Backend-utvecklare",
                new DateOnly(2021, 3, 1), new DateOnly(2024, 5, 1),
                "Ledde teamet om 8 personer och ökade konverteringen med 23 procent."),
        ],
        educations:
        [
            new Education("KTH", "Civilingenjör datateknik", new DateOnly(2014, 8, 1), new DateOnly(2019, 6, 1)),
        ],
        skills: [new Skill("C#", 5)],
        summary: "Erfaren backend-utvecklare med djup kunskap om betalsystem.",
        languages: [new SpokenLanguage("Svenska", LanguageProficiency.Native)],
        skillGroups: [],
        sections: []);

    // ===============================================================
    // Pass — an allowlisted body font at or above the size floor
    // ===============================================================

    [Fact]
    public async Task D3_ShouldPass_WhenBodyIsAnAllowlistedFontAtOrAboveTheFloor()
    {
        // A subset-tagged, style-suffixed body name so the FontNameNormalizer path is exercised
        // (ABCDEF+Arial-BoldMT → "arial"); a smaller, less-frequent heading run must not win.
        var d3 = await D3Async(WithRuns(
            new CvFontRun("ABCDEF+Arial-BoldMT", 11, 500),
            new CvFontRun("Arial", 16, 40)));

        d3.Verdict.ShouldBe(CriterionVerdict.Pass);
        var observation = d3.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;
        // The evidence resolves to the CLEAN allowlist name (never the subset-mangled raw name).
        observation.ShouldContain("Arial");
        observation.ShouldNotContain("ABCDEF");
        observation.ShouldContain("11");
    }

    // ===============================================================
    // Warn — non-allowlisted family (never Fail in v1)
    // ===============================================================

    [Fact]
    public async Task D3_ShouldWarn_WhenTheBodyFontIsNotAllowlisted()
    {
        var d3 = await D3Async(WithRuns(new CvFontRun("BCDEEE+Comic Sans MS", 11, 500)));

        d3.Verdict.ShouldBe(CriterionVerdict.Warn);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Pass);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Fail, "D3 fäller aldrig i v1 (exemplar-lista, aldrig en over-claim).");
        d3.Evidence.ShouldNotBeEmpty();
    }

    // ===============================================================
    // Warn — an allowlisted family but the body text is below the floor
    // ===============================================================

    [Fact]
    public async Task D3_ShouldWarn_WhenTheBodyFontIsAllowlistedButBelowTheSizeFloor()
    {
        var smallPt = Floor() - 2; // clearly under the readable-body floor
        var d3 = await D3Async(WithRuns(new CvFontRun("Calibri", smallPt, 500)));

        d3.Verdict.ShouldBe(CriterionVerdict.Warn);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Fail);
        d3.Evidence.ShouldNotBeEmpty();
    }

    // ===============================================================
    // Warn — non-allowlisted family AND below the floor (both fail)
    // ===============================================================

    [Fact]
    public async Task D3_ShouldWarn_WhenTheBodyFontIsNeitherAllowlistedNorAboveTheFloor()
    {
        var smallPt = Floor() - 2;
        var d3 = await D3Async(WithRuns(new CvFontRun("BCDEEE+Comic Sans MS", smallPt, 500)));

        d3.Verdict.ShouldBe(CriterionVerdict.Warn);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Fail);
    }

    // ===============================================================
    // Pass boundary — exactly at the floor (the floor is inclusive: < floor warns)
    // ===============================================================

    [Fact]
    public async Task D3_ShouldPass_WhenAnAllowlistedBodyFontSitsExactlyAtTheSizeFloor()
    {
        var d3 = await D3Async(WithRuns(new CvFontRun("Arial", Floor(), 500)));

        d3.Verdict.ShouldBe(CriterionVerdict.Pass, "golvet är inklusivt (>= golvet är Pass, endast < golvet varnar).");
    }

    // ===============================================================
    // NotAssessed — no font-run signal; NEVER Pass
    // ===============================================================

    [Fact]
    public async Task D3_ShouldNotAssess_WhenLayoutMetricsAreAbsent()
    {
        var d3 = await D3Async(layout: null);

        d3.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Pass);
        d3.Evidence.ShouldBeEmpty();
        d3.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task D3_ShouldNotAssess_WhenFontRunsAreNull()
    {
        // Analyzed geometry but no page carried letters → null font runs (a pre-#891 import
        // deserialises the same way) → NotAssessed, never Pass.
        var d3 = await D3Async(CvLayoutMetrics.Analyzed(200_000, 1, 40, fontRuns: null));

        d3.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Pass);
        d3.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task D3_ShouldNotAssess_WhenFontRunsAreEmpty()
    {
        var d3 = await D3Async(CvLayoutMetrics.Analyzed(200_000, 1, 40, fontRuns: []));

        d3.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Pass);
    }

    [Fact]
    public async Task D3_ShouldNotAssess_WhenTheFileIsADocx()
    {
        // NotApplicable geometry (a DOCX, D10) has no font runs → D3 NotAssessed, never Pass.
        var d3 = await D3Async(CvLayoutMetrics.NotApplicable(fileSizeBytes: 350_000));

        d3.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Pass);
        d3.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task D3_ShouldNotAssess_WhenTheParseFailed()
    {
        // A corrupt/encrypted PDF → Failed geometry, no font runs → NotAssessed.
        var d3 = await D3Async(CvLayoutMetrics.Failed(fileSizeBytes: 200_000));

        d3.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Pass);
    }

    // ===============================================================
    // NotAssessed on the canonical arm — no Pass-by-construction (unlike D1)
    // ===============================================================

    [Fact]
    public async Task D3_ShouldNotAssess_OnTheCanonicalArm_BecauseThereIsNoSourceFileGeometry()
    {
        var content = CanonicalContent();
        var linearized = ResumeContentLinearizer.Linearize(content);
        var context = CvReviewContext.FromCanonical(content, linearized, ResumeLanguage.Sv);

        var result = await Engine().ReviewAsync(
            context, RenderProfile.Ats, TestContext.Current.CancellationToken);
        var d3 = Verdict(result, "D3");

        d3.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
            "app-hanterat innehåll har ingen källfil-geometri; D3 saknar Pass-by-construction (till skillnad mot D1) → NotAssessed.");
        d3.Verdict.ShouldNotBe(CriterionVerdict.Pass);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Warn);
    }

    // ===============================================================
    // Modal body selection — a larger, rarer heading must not win
    // ===============================================================

    [Fact]
    public async Task D3_ShouldKeyOffTheModalBodyFont_NotTheLargerLessFrequentHeading()
    {
        // The heading (20 pt) is larger but carries far FEWER letters than the body (11 pt); the
        // body is the modal run, so the verdict + evidence must key off 11 pt, not 20.
        var d3 = await D3Async(WithRuns(
            new CvFontRun("Arial", 11, 800),
            new CvFontRun("Arial", 20, 60)));

        d3.Verdict.ShouldBe(CriterionVerdict.Pass);
        var observation = d3.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;
        // The verdict + evidence key off the modal body run (11 pt), never the larger heading (20 pt).
        observation.ShouldContain("11");
        observation.ShouldNotContain("20");
    }

    // ===============================================================
    // Exact-match guard — "Arial Narrow" must NOT false-Pass against "Arial"
    // ===============================================================

    [Fact]
    public async Task D3_ShouldWarn_WhenBodyIsArialNarrow_NeverFalsePassAgainstArial()
    {
        // "Arial Narrow" is a distinct condensed font; EXACT-equality normalisation must not fold
        // it onto "Arial" (a prefix/substring match would false-Pass). Load-bearing regression.
        var d3 = await D3Async(WithRuns(new CvFontRun("ArialNarrow", 11, 500)));

        d3.Verdict.ShouldBe(CriterionVerdict.Warn);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Pass,
            "ArialNarrow är inte Arial; exakt-matchning får aldrig fälla ihop dem till en Pass.");
    }

    // ===============================================================
    // Profile scoping — D3 is AtsOnly, absent from a Visual review
    // ===============================================================

    [Fact]
    public async Task D3_ShouldNotAppearInAVisualReview_BecauseItIsAtsOnly()
    {
        var visual = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(Resume(layoutMetrics: WithRuns(new CvFontRun("Arial", 11, 500)))),
            RenderProfile.Visual, TestContext.Current.CancellationToken);

        visual.Verdicts.ShouldNotContain(v => v.CriterionId == "D3");
    }

    // ===============================================================
    // Fail — the modal BODY font is an icon font (#957, the D3 Fail arm).
    // Size-independent: an icon font never carries readable text, so it fails
    // regardless of pt. The evidence cites the clean (subset-tag-stripped)
    // observed name and is ABOUT icon fonts; never any glyph or CV text.
    // ===============================================================

    [Fact]
    public async Task D3_ShouldFail_WhenTheModalBodyFontIsAnIconFont()
    {
        // A body set entirely in Wingdings renders as glyphs; an ATS reads symbols, not text.
        var d3 = await D3Async(WithRuns(new CvFontRun("Wingdings", 11, 500)));

        d3.Verdict.ShouldBe(CriterionVerdict.Fail);
        var observation = d3.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;
        // The clean font name is cited (parity with the D3 Pass-evidence test).
        observation.ShouldContain("Wingdings");
        // The copy is ABOUT icon fonts; that is the fragment separating this Fail from the generic
        // non-allowlisted Warn. Case-folded so later copy polish stays green (draft copy).
        observation.ToLowerInvariant().ShouldContain("ikon");
    }

    [Fact]
    public async Task D3_ShouldFail_WhenTheIconBodyFontIsSubsetTaggedAndStyleSuffixed()
    {
        // PdfPig hands back a subset tag + style suffix. The Fail evidence cites the CLEAN name
        // (subset tag stripped, e.g. "Wingdings-Regular"), never the mangled raw form; parity with
        // the D3 Pass-evidence citation ("ABCDEF+Arial-BoldMT" is cited as "Arial").
        var d3 = await D3Async(WithRuns(new CvFontRun("ABCDEF+Wingdings-Regular", 11, 500)));

        d3.Verdict.ShouldBe(CriterionVerdict.Fail);
        var observation = d3.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;
        observation.ShouldContain("Wingdings");
        observation.ShouldNotContain("ABCDEF");
    }

    [Theory]
    [InlineData("FontAwesome5Free-Solid", "FontAwesome")]  // Font Awesome, the ubiquitous icon webfont
    [InlineData("MaterialIcons-Regular", "MaterialIcons")] // Google Material Icons
    public async Task D3_ShouldFail_WhenTheModalBodyFontIsAModernIconWebfont(
        string rawBodyFont, string citedFragment)
    {
        var d3 = await D3Async(WithRuns(new CvFontRun(rawBodyFont, 11, 500)));

        d3.Verdict.ShouldBe(CriterionVerdict.Fail);
        var observation = d3.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;
        observation.ShouldContain(citedFragment);
        observation.ToLowerInvariant().ShouldContain("ikon");
    }

    // ===============================================================
    // D4 boundary (load-bearing regression) — a NON-MODAL icon run (an accent,
    // a bullet, a contact icon) never fails D3. Rubric D4: "Ikoner får finnas".
    // The Fail arm keys off the modal BODY font ONLY; non-modal icon runs are
    // D4's permitted territory, not D3's.
    // ===============================================================

    [Fact]
    public async Task D3_ShouldPass_WhenAnAccentIconRunExistsButTheModalBodyIsStandard()
    {
        // Arial carries the body (500 letters at 11 pt); a handful of Wingdings glyphs (30 letters
        // at 20 pt: section bullets / contact icons) are NON-modal. The verdict keys off the modal
        // body font, so this is a clean Pass.
        var d3 = await D3Async(WithRuns(
            new CvFontRun("Arial", 11, 500),
            new CvFontRun("Wingdings", 20, 30)));

        d3.Verdict.ShouldBe(CriterionVerdict.Pass);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Fail,
            "ikoner får finnas (rubrik D4): en icke-modal ikon-run fäller aldrig D3.");
        var observation = d3.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;
        observation.ShouldContain("Arial");
    }

    // ===============================================================
    // Deliberate under-claim — bare "Symbol" is EXCLUDED from the icon token
    // set (nonzero legit-font collision tail), so a Symbol-body CV falls through
    // to the existing non-allowlisted Warn, never Fail (§5: a false Fail on a
    // good CV is the over-claim sin; under-claim is the safe arm).
    // ===============================================================

    [Fact]
    public async Task D3_ShouldWarn_WhenTheBodyFontIsSymbol_NeverFail()
    {
        var d3 = await D3Async(WithRuns(new CvFontRun("Symbol", 11, 500)));

        d3.Verdict.ShouldBe(CriterionVerdict.Warn);
        d3.Verdict.ShouldNotBe(CriterionVerdict.Fail);
    }

    // ===============================================================
    // Profile scoping — the icon Fail is still AtsOnly, absent from a Visual
    // review (parity with the D3 AtsOnly test above).
    // ===============================================================

    [Fact]
    public async Task D3_IconFontFail_ShouldNotAppearInAVisualReview()
    {
        var visual = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(Resume(layoutMetrics: WithRuns(new CvFontRun("Wingdings", 11, 500)))),
            RenderProfile.Visual, TestContext.Current.CancellationToken);

        visual.Verdicts.ShouldNotContain(v => v.CriterionId == "D3");
    }
}
