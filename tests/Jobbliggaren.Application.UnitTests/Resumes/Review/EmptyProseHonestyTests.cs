using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// CV-pivot 2026-07-16 (scope 4) — the seven vacuous Passes stop.
///
/// <para>Klas's live repro: a CV that is a NAME plus the line "Jag är bäst!" — no experience,
/// no education, no skills — came back with two findings and NINE green Passes. Seven of those
/// Passes (A7, A9, C2, C3, C4, C6, C7) scan <c>ReviewText.AllProse</c>, which is EMPTY for that
/// CV, and each affirmed quality over a corpus with nothing in it: "Inga klyschor funna",
/// "Övervägande aktivt språk", "Inga misstänkta stavfel".</para>
///
/// <para>That is ADR 0109's defect class INVERTED: A8 claimed an ABSENCE it had not observed;
/// these claim a PRESENCE of quality they have not observed. Zero hits in zero text is not an
/// observation. The verdicts are WITHDRAWN to NotAssessed on an empty corpus — never upgraded
/// to Warn/Fail (grading emptiness as a language defect would misreport what was measured;
/// the missing CONTENT is A10/B1's subject, and their earned Fails survive below).</para>
/// </summary>
public class EmptyProseHonestyTests
{
    private static CvReviewEngine Engine() => new(
        RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
        AllCorrectSpellChecker(), RealAllowlist(), RealCvConventionsProvider(), RealParsingLexicon());

    /// <summary>The seven rules whose corpus is <c>ReviewText.AllProse</c>.</summary>
    public static TheoryData<string> ProseCriteria =>
        new("A7", "A9", "C2", "C3", "C4", "C6", "C7");

    // Each guard's aspect literal, criterion by criterion. This table is the mutation pin the
    // review gates demanded (test-writer Major, CONFIRMED by applied mutations): with only
    // "reason is non-blank" asserted, an ASPECT-SWAP between two guards (C7 saying "tonen",
    // C2 saying "stavningen") and a SYMMETRIC criterion-id-swap in the empty branches both
    // survived the full suites — the job-seeker would see the wrong aspect under the wrong
    // criterion, all green. Binding verdict[X] to X's OWN expected reason kills both at once,
    // and pins the reviewed §10 wording against silent drift.
    private static readonly Dictionary<string, string> AspectByCriterion =
        new(StringComparer.Ordinal)
        {
            ["A7"] = "klyschorna",
            ["A9"] = "personlighetsadjektiven",
            ["C2"] = "tonen",
            ["C3"] = "språket",
            ["C4"] = "perspektivet",
            ["C6"] = "förkortningarna",
            ["C7"] = "stavningen",
        };

    // Klas's repro verbatim (2026-07-16): a name and a boast. The segmenter classifies the name
    // as contact, carries "Jag är bäst!" as the unclassified Preamble (ADR 0109 — which never
    // enters AllProse), and leaves profile/experience/education/skills empty.
    private const string ContentFreeCv = "Anna Andersson\nJag är bäst!";

    private static async Task<CvReviewResult> ReviewAsync(string cvText)
    {
        return await Engine().ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(cvText)),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TheReproFixture_IsWhatKlasUploaded()
    {
        // Grounds the fixture: if the segmenter ever starts classifying the boast line as
        // profile/experience, every test below would assert against a different CV shape than
        // the repro — this pin makes that drift loud instead of silent.
        var parsed = ResumeFromCvText(ContentFreeCv);

        parsed.Content.Profile.ShouldBeNull();
        parsed.Content.Experience.ShouldBeEmpty();
        parsed.Content.Education.ShouldBeEmpty();
        parsed.Content.Skills.ShouldBeEmpty();
        parsed.Content.Preamble.ShouldNotBeNull();
        parsed.Content.Preamble.ShouldContain("Jag är bäst");
    }

    [Theory]
    [MemberData(nameof(ProseCriteria))]
    public async Task ProseRule_OnAContentFreeCv_IsNotAssessed_NeverAVacuousPass(string criterionId)
    {
        var result = await ReviewAsync(ContentFreeCv);

        // Before this fix: CriterionVerdict.Pass with structural evidence affirming quality
        // ("Inga klyschor…", "Saklig, neutral ton…") over an empty prose corpus.
        VerdictOf(result, criterionId).ShouldBe(CriterionVerdict.NotAssessed);
    }

    [Theory]
    [MemberData(nameof(ProseCriteria))]
    public async Task ProseRule_NotAssessedReason_IsTheCriterionsOwnCivicReason(string criterionId)
    {
        var result = await ReviewAsync(ContentFreeCv);
        var verdict = Verdict(result, criterionId);

        // EXACT-string pin against the shared helper with this criterion's own aspect. This is
        // deliberately not a "non-blank" smoke assert (the NotAssessed factory already enforces
        // that): equality is what kills an aspect-swap between two guards, a symmetric
        // criterion-id-swap (the swapped slot's reason then mismatches its expected aspect),
        // and any un-reviewed drift in the user-facing §10 wording. It also proves the guard
        // fired BECAUSE the prose corpus was empty — the engine's no-rule fallback and the
        // pinned-assessability path both carry the ASSET's reason, never this C# one. The
        // expected string contains no CV text, so PII-echo is excluded by the same equality
        // (parity A8PreambleHonestyTests).
        verdict.NotAssessedReason.ShouldBe(
            ReviewText.NoProseReason(AspectByCriterion[criterionId]));
        verdict.Evidence.ShouldBeEmpty();
    }

    [Fact]
    public async Task ContentFreeCv_TheEarnedFailsSurvive()
    {
        // Honesty must not delete working signals (the A8 lesson): the criteria whose SUBJECT
        // is the missing content keep their observed Fails.
        var result = await ReviewAsync(ContentFreeCv);

        VerdictOf(result, "A10").ShouldBe(CriterionVerdict.Fail);
        VerdictOf(result, "B1").ShouldBe(CriterionVerdict.Fail);
        VerdictOf(result, "B3").ShouldBe(CriterionVerdict.Fail);

        string.Join(" ", Verdict(result, "B1").Evidence.Select(e => e.ToString()))
            .ShouldContain("Saknar kärnsektion");
    }

    [Fact]
    public async Task ContentFreeCv_BandsContentStructureLanguageAtTheFloor()
    {
        var result = await ReviewAsync(ContentFreeCv);

        Band(result, RubricCategory.Content).ShouldBe(ScoreBandLabel.NotReady);
        Band(result, RubricCategory.Structure).ShouldBe(ScoreBandLabel.NotReady);

        // Before this fix the LANGUAGE category banded TopTier (five vacuous Passes at 100 %
        // credit) on a CV with no language in it. All seven withdrawn → the category has no
        // assessed criterion left → the engine's existing all-NotAssessed rule bands the floor.
        Band(result, RubricCategory.Language).ShouldBe(ScoreBandLabel.NotReady);
    }

    [Fact]
    public async Task ContentFreeCv_TripsNoCriticalFails_ThePinnedStatusQuo()
    {
        // MEASURED STATUS QUO, pinned deliberately: criticalFailIds = [A1, B4, C1, D1], and a
        // content-free CV trips none of them (A1/C1 NotAssessed, B4/D1 genuinely Pass). Whether
        // emptiness itself should be a CRITICAL fail is a product call teed up to Klas (a new
        // content-presence criterion = rubric MAJOR bump — CTO re-bind, CV-pivot PR 1). If that
        // ships, this pin changes in that PR — consciously, not as drift.
        var result = await ReviewAsync(ContentFreeCv);

        result.CriticalFails.ShouldBeEmpty();
    }

    // ── Positive controls: the guard changes ONLY the empty-corpus branch ──

    [Theory]
    [MemberData(nameof(ProseCriteria))]
    public async Task ProseRule_OnPopulatedProse_IsStillAssessed(string criterionId)
    {
        // The strong default fixture carries profile + experience prose. Every prose rule must
        // still assess it — a guard that over-fires on non-empty prose would withdraw working
        // signals, and would also change evidence strings (re-keying stored finding statuses).
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(Resume()),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        VerdictOf(result, criterionId).ShouldNotBe(CriterionVerdict.NotAssessed);
    }

    [Theory]
    [MemberData(nameof(ProseCriteria))]
    public async Task ProseRule_ProfileOnlyProse_IsStillAssessed(string criterionId)
    {
        // One line of profile prose IS a gradeable corpus — the guard keys on AllProse being
        // empty, never on "experience missing" alone.
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(Resume(
                profile: "Jag bygger driftsäkra betaltjänster i .NET.",
                experience: [],
                education: [],
                skills: [])),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        VerdictOf(result, criterionId).ShouldNotBe(CriterionVerdict.NotAssessed);
    }

    [Theory]
    [MemberData(nameof(ProseCriteria))]
    public async Task ProseRule_ExperienceOnlyProse_IsStillAssessed(string criterionId)
    {
        // The mirror boundary: experience text alone also populates the corpus — the guard
        // requires BOTH halves empty.
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(Resume(
                profile: null,
                education: [],
                skills: [])),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        VerdictOf(result, criterionId).ShouldNotBe(CriterionVerdict.NotAssessed);
    }

    [Theory]
    [MemberData(nameof(ProseCriteria))]
    public async Task ProseRule_MinimalNonEmptyProse_IsStillAssessed(string criterionId)
    {
        // Near-boundary control (test-writer Minor): the other positive controls jump from ""
        // straight to full sentences, so a guard mutated to fire on short-but-non-empty prose
        // (e.g. `|| prose.Trim().Length < 5`) would survive them. One four-character word IS a
        // gradeable corpus — the guard keys on EMPTY, never on "too short to bother".
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(Resume(
                profile: "Jag.",
                experience: [],
                education: [],
                skills: [])),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        VerdictOf(result, criterionId).ShouldNotBe(CriterionVerdict.NotAssessed);
    }

    private static ScoreBandLabel Band(CvReviewResult result, RubricCategory category) =>
        result.Categories.Single(c => c.Category == category).Band;
}
