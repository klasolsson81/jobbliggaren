using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-6a (#655, ADR 0093 §D4, CTO-bind D-G) — B5 "Konsekvent formatering", the
/// GEOMETRY-FREE bullet-marker consistency criterion. Driven through the REAL
/// <see cref="CvReviewEngine"/> with an all-correct spell-checker stub so C7 (spelling) can never
/// interfere with the B5 verdict under test. Two verdicts only: <b>Warn</b> when the experience
/// descriptions mix 2+ distinct lead markers, else <b>NotAssessed</b> — NEVER Pass, since a clean
/// marker set does not prove the fonts/heading-levels B5 also covers were checked (that needs PDF
/// geometry the text parse cannot see; the honest ceiling, ADR 0071 OQ3).
///
/// The load-bearing property is ARM-INDEPENDENCE: the SAME logical content scored via
/// <see cref="CvReviewContext.FromParsed"/> (staging) and <see cref="CvReviewContext.FromCanonical"/>
/// (canonical) yields the SAME B5 verdict — which is what makes the styleOnly "Ignored" decision
/// reachable end-to-end (the canonical SetFindingStatus recompute matches the review the user saw).
/// </summary>
public class B5ConsistentFormattingRuleTests
{
    // Every bullet glyph B5 recognises as a lead marker — the evidence must never echo any of them.
    private const string BulletGlyphs = "•◦‣·●○▪▫■–—-*";

    private static CvReviewEngine Engine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> B5Async(CvReviewContext context) =>
        Verdict(
            await Engine().ReviewAsync(context, RenderProfile.Ats, TestContext.Current.CancellationToken),
            "B5");

    private static Task<CvCriterionVerdict> B5FromParsedAsync(params string[] bullets) =>
        B5Async(CvReviewContext.FromParsed(
            Resume(experience: [Experience(bullets: bullets)])));

    // ===============================================================
    // (a) Mixed markers → Warn (evidence never echoes the raw glyphs)
    // ===============================================================

    [Fact]
    public async Task B5_ShouldWarn_WhenDescriptionsMixTwoDistinctLeadMarkers()
    {
        var b5 = await B5FromParsedAsync("• Ledde teamet", "- Ansvarade för budget");

        b5.Verdict.ShouldBe(CriterionVerdict.Warn, "två olika punktsymboler = inkonsekvens per definition.");
        var observation = b5.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;
        observation.ShouldNotBeNullOrWhiteSpace();
        foreach (var glyph in BulletGlyphs)
        {
            observation.Contains(glyph).ShouldBeFalse(
                $"B5-bevis får inte återge den råa punktsymbolen '{glyph}' (civic no-em-dash m.m.).");
        }
    }

    // ===============================================================
    // (b) A single consistent marker → NotAssessed (NEVER Pass)
    // ===============================================================

    [Fact]
    public async Task B5_ShouldNotAssess_WhenAllDescriptionsUseTheSameSingleMarker()
    {
        var b5 = await B5FromParsedAsync("• Ledde teamet", "• Ansvarade för budget");

        b5.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
            "en enhetlig punktstil bevisar inte att typsnitt/rubriknivåer är konsekventa (ärlig tak).");
        b5.Verdict.ShouldNotBe(CriterionVerdict.Pass, "B5 rapporterar ALDRIG Pass geometri-fritt.");
        b5.NotAssessedReason.ShouldNotBeNullOrWhiteSpace();
    }

    // ===============================================================
    // (c) No bulleted lines → NotAssessed
    // ===============================================================

    [Fact]
    public async Task B5_ShouldNotAssess_WhenNoDescriptionLineStartsWithAMarker()
    {
        var b5 = await B5FromParsedAsync("Ledde teamet om 8 personer", "Ansvarade för budget");

        b5.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        b5.Verdict.ShouldNotBe(CriterionVerdict.Pass);
    }

    // ===============================================================
    // (d) A marker glyph glued to a word (no following space) is NOT a bullet
    // ===============================================================

    [Fact]
    public async Task B5_ShouldNotCountAGlyphGluedToAWord_AsALeadMarker()
    {
        // "• Ledde" is a real bullet (marker + space); "*viktigt*" is markdown emphasis glued to the
        // word (marker + non-space) → NOT a marker. Only {•} remains → 1 distinct → NotAssessed. Were
        // the glued "*" counted, we'd get {•,*} = 2 → Warn — so NotAssessed proves it is not counted.
        var b5 = await B5FromParsedAsync("• Ledde teamet", "*viktigt* för leveransen");

        b5.Verdict.ShouldBe(CriterionVerdict.NotAssessed,
            "en punktsymbol limmad mot ett ord (utan efterföljande blanksteg) räknas inte som bullet.");
    }

    // ===============================================================
    // (e) ARM-INDEPENDENCE — the load-bearing Ignored-reachability property
    // ===============================================================

    [Fact]
    public async Task B5_ShouldYieldTheSameVerdict_AcrossTheStagingAndCanonicalArms()
    {
        // The SAME logical content — a description mixing "•" and "-" — scored on BOTH arms must
        // produce the SAME B5 verdict. This is what makes the styleOnly "Ignored" decision reachable
        // e2e: the canonical SetFindingStatus recompute matches the staging review the user saw.
        const string mixedDescription = "• Ledde teamet\n- Ansvarade för budget";

        var stagingB5 = await B5FromParsedAsync("• Ledde teamet", "- Ansvarade för budget");

        var canonicalContent = new ResumeContent(
            new PersonalInfo("Anna Andersson", "anna@example.se", "070-123 45 67", "Stockholm"),
            experiences:
            [
                new Experience("Acme AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 1, 1), mixedDescription),
            ]);
        var linearized = ResumeContentLinearizer.Linearize(canonicalContent);
        var canonicalB5 = await B5Async(
            CvReviewContext.FromCanonical(canonicalContent, linearized, ResumeLanguage.Sv));

        stagingB5.Verdict.ShouldBe(CriterionVerdict.Warn, "det blandade underlaget ska ge Warn på staging-armen.");
        canonicalB5.Verdict.ShouldBe(stagingB5.Verdict,
            "B5 är arm-oberoende — canonical-recompute måste matcha staging-granskningen (Ignored-nåbarhet).");
    }
}
