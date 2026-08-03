using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #1060 road 3 — the date-model widening's acceptance measured on the REVIEW side (senior-cto-advisor
/// re-bind 2026-08-02, (S1)). The segmenter-side result — "Organization is now correctly null" — is
/// the half that was never the live defect; this class pins the half that was.
///
/// <para><b>What was live, and it is measured rather than derived.</b> On the three-line
/// "Title / Company / Dates" layout the employer is real, so nothing fabricated the date row into
/// the organisation slot and the organisation-equality test could not fire on it. Neither half of
/// <c>ReviewText</c>'s period union modelled the four forms, so the user's employment dates reached
/// <c>ExperienceBullets</c> and were scored as prose. Run against <c>b637b691</c>, before any edit:</para>
///
/// <code>
/// jan 2020 – dec 2024    A1 Warn  A2 Warn  A6 Warn   all three citing the date row
/// 2020 – 2024 (heltid)   A1 Warn  A2 Warn  A6 Warn   all three citing the date row
/// 2020/01 – 2024/12      A1 PASS  A2 Warn  A6 PASS   A1 note "kvantifierad uppgift"
/// 2020 –                 A1 Warn  A2 Warn  A6 Warn   all three citing the date row
/// </code>
///
/// <para><b>The <c>YYYY/MM</c> row is the sharp one and the reason this class exists.</b>
/// <c>DateRange</c> modelled that form on neither endpoint, so <c>StripDates</c> left "/01" and
/// "/12" unmasked, <c>ContainsMeasurableDigit</c> returned true, and A1 emitted an affirmative
/// <b>Pass</b> whose cited evidence is the user's employment dates under the note "kvantifierad
/// uppgift". The product asserted she had quantified a result, grounded entirely in her dates.
/// That is CLAUDE.md §5's <i>"a CV verdict without cited textual evidence"</i> inverted — a verdict
/// citing a span that is not prose — and it is the defect <c>ReviewText</c>'s own header claims
/// #487 fixed. On this layout, with this notation, that fix did not hold.</para>
///
/// <para><b>Why the criterion table could not catch it.</b> Measured on the same run: the layout
/// corpus reports A1/A2/A6 as NotAssessed on all 270 cases, because no corpus case yields scorable
/// bullets. A corpus reading is therefore not evidence about this class in either direction, and
/// an acceptance argued from "the criterion table did not move" would be measuring the wrong
/// instrument. These pins are the instrument.</para>
///
/// <para><b>What is asserted is the ABSENCE of the date row from the evidence, not a fixed
/// verdict.</b> A verdict is a rubric-data-driven ratio over the bullet set; pinning "A1 Pass"
/// would couple this class to threshold data it has no business owning. What must never happen
/// again is a criterion CITING the user's dates, so that is what each row asserts, alongside the
/// prose bullet surviving — because "suppress everything" would satisfy the first half alone and
/// degrade every A-criterion to NotAssessed.</para>
/// </summary>
public class DateModelWideningReviewSideTests
{
    private const string Bullet = "Ökade konverteringen med 23 procent.";

    private static CvReviewEngine Engine() => new(
        RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
        AllCorrectSpellChecker(), RealAllowlist(),
        RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvReviewResult> ReviewThreeLineLayoutAsync(string dateLine)
    {
        // The real segmenter end to end — no hand-built ReviewableExperience (CLAUDE.md §5, Tests).
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            {dateLine}
            {Bullet}
            """;

        return await Engine().ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(cv)),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("jan 2020 – dec 2024")]
    [InlineData("2020 – 2024 (heltid)")]
    [InlineData("2020/01 – 2024/12")]
    [InlineData("2020 –")]
    // The two forms the model already reached, carried along as the control: they were never
    // scored, and a change that started scoring them would be the same defect arriving from the
    // other direction.
    [InlineData("2013 - 2021")]
    [InlineData("2020-06 – 2024-03")]
    public async Task NoCriterionCitesTheUsersEmploymentDates_OnTheThreeLineLayout(string dateLine)
    {
        var result = await ReviewThreeLineLayoutAsync(dateLine);

        foreach (var id in new[] { "A1", "A2", "A6" })
        {
            var verdict = result.Verdicts.Single(v => v.CriterionId == id);
            foreach (var quote in verdict.Evidence.OfType<TextSpanEvidence>().Select(e => e.Span.Quote))
            {
                quote.ShouldNotBe(dateLine,
                    $"{id} cited the user's employment dates as though they were prose — CLAUDE.md " +
                    $"§5's cited-evidence rule inverted. Before the widening this fired on all four " +
                    $"unmodelled forms, and on YYYY/MM it was an affirmative Pass.");
            }
        }
    }

    [Theory]
    [InlineData("jan 2020 – dec 2024")]
    [InlineData("2020 – 2024 (heltid)")]
    [InlineData("2020/01 – 2024/12")]
    [InlineData("2020 –")]
    public async Task TheProseBulletIsStillScored_SoSuppressionDidNotBecomeSilence(string dateLine)
    {
        // The other half of the acceptance, and it is not a formality: removing the date row from
        // the bullet set must leave the REAL bullet in it. If suppression over-reached, every
        // A-criterion would degrade to NotAssessed and the pin above would pass vacuously — a
        // criterion that assesses nothing cites nothing.
        var result = await ReviewThreeLineLayoutAsync(dateLine);

        foreach (var id in new[] { "A1", "A2", "A6" })
        {
            var verdict = result.Verdicts.Single(v => v.CriterionId == id);

            verdict.Verdict.ShouldNotBe(CriterionVerdict.NotAssessed,
                $"{id} must still have a scorable bullet — the prose one.");
            verdict.Evidence.OfType<TextSpanEvidence>().Select(e => e.Span.Quote)
                .ShouldContain(Bullet, $"{id} must cite the prose bullet, which is the only bullet left.");
        }
    }

    [Fact]
    public async Task B5_ShouldNotReadTheUsersDateRowAsASecondBulletStyle()
    {
        // THE LIVE ESCAPE NO TEST IN THE TREE PINNED, now measured rather than derived. B5 counts
        // distinct leading bullet glyphs and Warns at two. Its LeadMarker has two guards: one needs
        // a marker glyph followed by whitespace, and the second nulls a marker whose remainder
        // PeriodParser parses (written for "- 2020 – nuvarande"). A row clearing BOTH — a marker
        // glyph AND a remainder PeriodParser refuses — is "– jan 2020 – dec 2024", and PeriodParser
        // refuses every month-name form.
        //
        // MEASURED before the widening, on a CV whose only other bullet uses "•":
        //   B5 Warn — "Blandade punktsymboler i beskrivningarna. Välj en enhetlig punktstil."
        // The second "bullet style" B5 counted was the user's date row. She would have been told to
        // unify punctuation she had not varied.
        //
        // The widening closes it upstream: DescriptionLines suppresses the row, so LeadMarker is
        // never offered it and no second glyph exists to count. Note the mechanism — this is NOT a
        // change to B5 or to LeadMarker, which are untouched.
        const string cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            – jan 2020 – dec 2024
            • {Bullet}
            """;

        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(cv)),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        result.Verdicts.Single(v => v.CriterionId == "B5").Verdict
            .ShouldBe(CriterionVerdict.NotAssessed,
                "the user's date row is not a bullet style, so it must not make her CV look " +
                "inconsistently punctuated.");
    }

    [Fact]
    public async Task B5_StillWarns_WhenTheCvGenuinelyMixesTwoBulletStyles()
    {
        // The counterfactual, without which the pin above proves only that B5 can return
        // NotAssessed. Two REAL bullet styles on two real prose bullets must still Warn — the
        // widening removes a false positive, it does not disarm the criterion.
        const string cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            – jan 2020 – dec 2024
            • {Bullet}
            - Byggde en ny betaltjänst.
            """;

        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(cv)),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        result.Verdicts.Single(v => v.CriterionId == "B5").Verdict
            .ShouldBe(CriterionVerdict.Warn,
                "two genuine bullet glyphs are still mixed punctuation.");
    }
}
