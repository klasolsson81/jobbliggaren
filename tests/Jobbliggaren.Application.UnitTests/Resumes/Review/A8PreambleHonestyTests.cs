using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #844 — A8 stops lying.
///
/// <para>A8 reads the STRUCTURED content, never RawText (<c>context.RawText</c> is a citation
/// substrate — a needle-in-haystack lookup whose needle already came from <c>Content</c>). So a CV
/// that opened with an un-headed summary — prose the segmenter dropped — was told, as a HARD FAIL
/// with structural evidence, that "Profiltext saknas helt." Her words were sitting verbatim in
/// <c>raw_text</c>, unread.</para>
///
/// <para>That is a claim asserted about something the engine never inspected: the same defect class
/// as 8b.4b's Blocker B2. The verdict is WITHDRAWN, not replaced — reduced precision is marked "not
/// assessed", never mis-reported (CLAUDE.md §5).</para>
/// </summary>
public class A8PreambleHonestyTests
{
    private static CvReviewEngine Engine() => new(
        RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
        AllCorrectSpellChecker(), RealAllowlist(), RealCvConventionsProvider(), RealParsingLexicon());

    private const string UnheadedSummaryCv =
        """
        Anna Andersson
        anna.andersson@example.com
        070-123 45 67

        Erfaren backend-utvecklare med tio år i betalbranschen. Jag bygger driftsäkra
        tjänster i .NET och trivs närmast produktionen.

        Arbetslivserfarenhet
        Backend-utvecklare — Acme AB
        2021 - 2024
        Ansvarade för betaltjänster och ökade genomströmningen med 30 procent.

        Utbildning
        Civilingenjör — KTH
        2016 - 2021
        """;

    private const string NoSummaryCv =
        """
        Anna Andersson
        anna.andersson@example.com
        070-123 45 67

        Arbetslivserfarenhet
        Backend-utvecklare — Acme AB
        2021 - 2024
        Ansvarade för betaltjänster och ökade genomströmningen med 30 procent.

        Utbildning
        Civilingenjör — KTH
        2016 - 2021
        """;

    private static async Task<CvCriterionVerdict> A8Async(string cvText)
    {
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(cvText)),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        return result.Verdicts.Single(v => v.CriterionId == "A8");
    }

    [Fact]
    public async Task A8_UnheadedSummary_IsNotAssessed_NeverAFalseFail()
    {
        var a8 = await A8Async(UnheadedSummaryCv);

        // Before #844 this was CriterionVerdict.Fail with the evidence "Profiltext saknas helt." —
        // asserted, as a hard Fail, about a summary the user had written.
        a8.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
    }

    [Fact]
    public async Task A8_UnheadedSummary_NeverClaimsTheProfileIsMissing()
    {
        var a8 = await A8Async(UnheadedSummaryCv);

        var said = string.Join(" ", a8.Evidence.Select(e => e.ToString())) + " " + a8.NotAssessedReason;
        said.ShouldNotContain("saknas helt");
    }

    [Fact]
    public async Task A8_UnheadedSummary_ReasonCarriesNoCvText()
    {
        // The preamble is the most personnummer-dense region of a CV. A verdict's reason string is
        // structural, never a PII channel — it says THAT text was unclassifiable, never WHAT it said.
        var a8 = await A8Async(UnheadedSummaryCv);

        a8.NotAssessedReason.ShouldNotBeNull();
        a8.NotAssessedReason.ShouldNotContain("betalbranschen");
        a8.NotAssessedReason.ShouldNotContain("driftsäkra");
        a8.Evidence.ShouldBeEmpty();
    }

    [Fact]
    public async Task A8_GenuinelyNoSummary_StillFails_TheEarnedFailSurvives()
    {
        // The arm that must NOT be withdrawn. Here the preamble is fully accounted for (name, mail,
        // phone), so the absence of a profile is genuinely OBSERVED and the Fail is earned.
        // Withdrawing this too would delete a working signal — a regression dressed as honesty.
        var a8 = await A8Async(NoSummaryCv);

        a8.Verdict.ShouldBe(CriterionVerdict.Fail);
        string.Join(" ", a8.Evidence.Select(e => e.ToString())).ShouldContain("Profiltext saknas helt");
    }

    [Fact]
    public async Task A8_HeadedProfile_IsAssessedExactlyAsBefore()
    {
        const string headed =
            """
            Anna Andersson
            anna.andersson@example.com

            Profil
            Erfaren backend-utvecklare med fokus på betaltjänster och driftsäkerhet.

            Arbetslivserfarenhet
            Backend-utvecklare — Acme AB
            2021 - 2024
            Ansvarade för betaltjänster och ökade genomströmningen med 30 procent.

            Utbildning
            Civilingenjör — KTH
            2016 - 2021
            """;

        (await A8Async(headed)).Verdict.ShouldBe(CriterionVerdict.Pass);
    }

    /// <summary>
    /// THE guard that keeps Variant A from re-entering through the back door.
    ///
    /// <para><c>ReviewText.AllProse</c> is the corpus A7 (clichés), A9 (soft skills) and the language
    /// rules scan. Routing the unclassified preamble into it would have the engine GRADE an address
    /// block or OCR noise as the user's own writing — i.e. classify it after all, which is the one
    /// thing the carrier exists to refuse.</para>
    /// </summary>
    // The cliché is DERIVED from the shipped lexicon, never guessed. A first attempt at this test
    // invented a phrase ("Teamplayer") that is not in cliche-list.v2.json, so A7 could never have
    // fired on it and the test passed for the wrong reason — it survived the very mutation it exists
    // to catch. A guard whose trigger is imagined does not guard anything.
    private const string RealCliche = "Driven lagspelare";

    /// <summary>
    /// THE guard that keeps Variant A from re-entering through the back door — and the single most
    /// important assertion in this change.
    ///
    /// <para><c>ReviewText.AllProse</c> is the corpus A7 (clichés), A9 (soft skills) and the language
    /// rules scan. Routing the unclassified preamble into it would have the engine GRADE an address
    /// block, a tagline or OCR noise as the user's own writing — i.e. classify it after all, which is
    /// exactly what the carrier exists to refuse.</para>
    ///
    /// <para>It comes with a POSITIVE CONTROL: the identical sentence under a "Profil" heading MUST
    /// trip A7. Without that control, this test would also pass if A7 were simply broken — and it
    /// would be pinning nothing.</para>
    /// </summary>
    [Fact]
    public async Task Preamble_IsNeverGradedAsProse_WhileTheSameSentenceUnderAHeadingIs()
    {
        const string clicheAbovveTheFirstHeading =
            $"""
            Anna Andersson
            anna.andersson@example.com

            {RealCliche} som söker nya utmaningar.

            Arbetslivserfarenhet
            Backend-utvecklare — Acme AB
            2021 - 2024
            Ansvarade för betaltjänster och ökade genomströmningen med 30 procent.

            Utbildning
            Civilingenjör — KTH
            2016 - 2021
            """;

        const string sameClicheUnderAProfilHeading =
            $"""
            Anna Andersson
            anna.andersson@example.com

            Profil
            {RealCliche} som söker nya utmaningar.

            Arbetslivserfarenhet
            Backend-utvecklare — Acme AB
            2021 - 2024
            Ansvarade för betaltjänster och ökade genomströmningen med 30 procent.

            Utbildning
            Civilingenjör — KTH
            2016 - 2021
            """;

        // POSITIVE CONTROL: headed, the sentence IS the user's profile, so A7 SEES it and cites it.
        //
        // A single cliché sits under the rubric's passBelowCount, so the VERDICT is Pass either way
        // — the observable difference is the EVIDENCE: A7 cites the phrase it found, "so the pass is
        // transparent, never a hidden flag". That citation is therefore the exact signal for "this
        // text entered the prose corpus", and it is what this test pins. Without this control the
        // guard below would also pass if A7 were simply broken, and it would be pinning nothing.
        var headed = ResumeFromCvText(sameClicheUnderAProfilHeading);
        headed.Content.Profile.ShouldNotBeNull();

        var headedResult = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(headed), RenderProfile.Ats, TestContext.Current.CancellationToken);

        var headedA7Said = string.Join(
            " ", headedResult.Verdicts.Single(v => v.CriterionId == "A7").Evidence.Select(e => e.ToString()));

        headedA7Said.ShouldContain(RealCliche);

        // THE GUARD: un-headed, the identical sentence is UNCLASSIFIED. The engine was never told it
        // is prose the user wrote about herself, so it must not enter the prose corpus and must not
        // be graded — not by A7, not by A9, not by anything.
        var unheaded = ResumeFromCvText(clicheAbovveTheFirstHeading);
        unheaded.Content.Preamble.ShouldNotBeNull();
        unheaded.Content.Preamble.ShouldContain(RealCliche);

        var unheadedResult = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(unheaded), RenderProfile.Ats, TestContext.Current.CancellationToken);

        string.Join(" ", unheadedResult.Verdicts.SelectMany(v => v.Evidence.Select(e => e.ToString())))
            .ShouldNotContain(RealCliche);
    }

    // ===============================================================
    // #1060 — the same rule, one layer up: the CANONICAL arm
    // ===============================================================

    /// <summary>
    /// The regression pin for #844-at-one-layer-up, and the reason the gate retirement and the
    /// carrier had to ship in one PR.
    ///
    /// <para>Before #1060 an un-headed summary could never reach a saved CV: the preamble gate
    /// held the whole import in staging. Retiring that gate lets it through, and the canonical
    /// adapter used to hard-code <c>Preamble: null</c> — justified at the time, because an
    /// app-BUILT CV really has no region above its first heading. An IMPORT-origin CV does. So
    /// lifting the gate without this would have had A8 read <c>Profile == null</c> (the file
    /// carried no "Profil" heading) beside <c>Preamble == null</c>, take its second arm, and
    /// emit the hard Fail "Profiltext saknas helt." about the summary the user wrote and the
    /// product is now storing. That is #844's headline defect, resurrected by its own fix.</para>
    /// </summary>
    private static ResumeContent ImportedContentWithPreamble(string? preamble) => new(
        new PersonalInfo("Anna Andersson", "anna.andersson@example.com", "070-123 45 67", "Stockholm"),
        experiences:
        [
            new Experience(
                "Acme AB", "Backend-utvecklare", null, null,
                "Ansvarade för betaltjänster och ökade genomströmningen med 30 procent.", "2021 - 2024"),
        ],
        educations: [new Education("KTH", "Civilingenjör", null, null, "2016 - 2021")],
        skills: [new Skill("C#", null)],
        // No Summary: the file had no "Profil" heading, which is precisely the population this
        // pin is about. Give it one and A8 takes a different arm and the test proves nothing.
        summary: null,
        languages: [],
        skillGroups: [],
        sections: [],
        preamble: preamble);

    private static async Task<CvCriterionVerdict> CanonicalA8Async(string? preamble)
    {
        var content = ImportedContentWithPreamble(preamble);
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromCanonical(
                content, ResumeContentLinearizer.Linearize(content), ResumeLanguage.Sv),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        return result.Verdicts.Single(v => v.CriterionId == "A8");
    }

    [Fact]
    public async Task CanonicalA8_ImportOriginCvWithPreamble_IsNotAssessed_NeverAFalseFail()
    {
        var a8 = await CanonicalA8Async(
            "Erfaren backend-utvecklare med tio år i betalbranschen. Jag bygger driftsäkra tjänster.");

        a8.Verdict.ShouldBe(CriterionVerdict.NotAssessed);
        var said = string.Join(" ", a8.Evidence.Select(e => e.ToString())) + " " + a8.NotAssessedReason;
        said.ShouldNotContain("saknas helt");
    }

    [Fact]
    public async Task CanonicalA8_ImportOriginCvWithPreamble_ReasonCarriesNoCvText()
    {
        // Parity with the staging arm: the preamble is the most personnummer-dense region of a
        // CV, so a verdict's reason says THAT text was unclassifiable, never WHAT it said.
        var a8 = await CanonicalA8Async(
            "Erfaren backend-utvecklare med tio år i betalbranschen. Jag bygger driftsäkra tjänster.");

        a8.NotAssessedReason.ShouldNotBeNull();
        a8.NotAssessedReason.ShouldNotContain("betalbranschen");
        a8.NotAssessedReason.ShouldNotContain("driftsäkra");
        a8.Evidence.ShouldBeEmpty();
    }

    /// <summary>
    /// The counterfactual that makes the pin above mean something. Identical content, preamble
    /// absent — which is every TEMPLATE-origin CV — and the earned Fail must survive. Without
    /// this row the NotAssessed test would also pass if A8 had simply stopped failing, and it
    /// would be pinning nothing. Withdrawing this arm too would delete a working signal, which
    /// is a regression dressed as honesty.
    /// </summary>
    [Fact]
    public async Task CanonicalA8_TemplateOriginCvWithNoPreamble_StillFails_TheEarnedFailSurvives()
    {
        var a8 = await CanonicalA8Async(preamble: null);

        a8.Verdict.ShouldBe(CriterionVerdict.Fail);
        string.Join(" ", a8.Evidence.Select(e => e.ToString())).ShouldContain("Profiltext saknas helt");
    }

    /// <summary>
    /// The canonical-arm twin of <see cref="Preamble_IsNeverGradedAsProse_WhileTheSameSentenceUnderAHeadingIs"/>.
    /// The carrier reaching <c>ResumeContent</c> must not turn it into graded prose: A7, A9 and
    /// the language rules read <c>ReviewText.AllProse</c>, and routing an address block or OCR
    /// noise there would grade it as the user's own writing — the auto-classification the
    /// carrier exists to refuse, arriving through the back door on the arm that renders CVs.
    /// </summary>
    [Fact]
    public async Task CanonicalPreamble_IsNeverGradedAsProse()
    {
        var content = ImportedContentWithPreamble($"{RealCliche} som söker nya utmaningar.");

        var result = await Engine().ReviewAsync(
            CvReviewContext.FromCanonical(
                content, ResumeContentLinearizer.Linearize(content), ResumeLanguage.Sv),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        string.Join(" ", result.Verdicts.SelectMany(v => v.Evidence.Select(e => e.ToString())))
            .ShouldNotContain(RealCliche);
    }
}
