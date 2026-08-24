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
    [InlineData("2020 –")]
    // The two forms the model already reached, carried along as the control: they were never
    // scored, and a change that started scoring them would be the same defect arriving from the
    // other direction.
    [InlineData("2013 - 2021")]
    [InlineData("2020-06 – 2024-03")]
    // THE ACADEMIC YEAR, and it is here because this is the row where the defect was actually
    // OBSERVED this session rather than predicted. Measured at 83d7a6b3, where a structural month
    // class had been applied to both alternations and this form stopped being a date row:
    //   A1 Pass · quote "2019-20 – 2021" · note "kvantifierad uppgift"
    // The product asserting the user quantified a result out of her employment dates — the §5 class
    // this whole PR exists to close, re-opened on a population the PR had not looked at. The
    // characterisation table pins the CAUSE (IsDateOnlyLine and the stored value); this pins the
    // consequence at the altitude where it harms someone.
    [InlineData("2019-20 – 2021")]
    [InlineData("2019-20 – nuvarande")]
    // THE YEAR-FIRST SLASH POPULATION, moved here by ADR 0136 from a Fact that asserted the exact
    // opposite. It is SIX rows, not the one #1195's title names: the three MIXED rows are equally
    // unsuppressed, because a trailing "/NN" residue keeps the reduced line non-empty even where
    // DateRange matched a prefix and stored a readable Period. Decision D′ protected those three on
    // the VALUE axis and left them citing the user's dates on the LINE axis; the two axes are
    // independent and this class only ever measured one of them.
    [InlineData("2020/01 – 2024/12")]
    [InlineData("2008/09 – 2011/12")]
    [InlineData("2019/20 – 2021")]
    [InlineData("2018 – 2019/20")]
    [InlineData("2020 – 2024/12")]
    [InlineData("2020-06 – 2024/12")]
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
    [InlineData("2020 –")]
    // The academic year gets the anti-vacuity partner too: an absence assertion passes trivially if
    // the criterion assesses nothing, and that is exactly the shape the row above has.
    [InlineData("2019-20 – 2021")]
    // The six year-first slash rows get it for the same reason, and they need it most: they are the
    // rows ADR 0136 newly suppresses, so "no criterion cites the date row" would pass vacuously if
    // the suppression had swallowed the prose bullet with it.
    [InlineData("2020/01 – 2024/12")]
    [InlineData("2008/09 – 2011/12")]
    [InlineData("2019/20 – 2021")]
    [InlineData("2018 – 2019/20")]
    [InlineData("2020 – 2024/12")]
    [InlineData("2020-06 – 2024/12")]
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
    public async Task B6_NowWarnsOnAGenuinelyMixedGranularityCv_AndThatIsIntentional()
    {
        // (S4) obligation 7 — THE DIRECTION THE WIDENING MAKES B6 STRICTER, pinned in the same
        // breath as the direction it makes B6 kinder, because measuring one polarity and deriving
        // the other is how this lane keeps producing claims true of their evidence.
        //
        // MEASURED before the widening: Pass. But that Pass existed only because the month-named
        // entry was EXCLUDED from the format set — PeriodParser could not read it, so B6 saw one
        // token. A Pass produced by under-coverage is a NotAssessed wearing a Pass. Now the CV is
        // read whole and is genuinely {MM/YYYY, YYYY}: one job written to the month, one to the year.
        //
        // The Warn is B6 becoming CORRECT, not stricter, and the threshold behind it
        // (maxDistinctDateFormats) stays in versioned rubric data where §5 requires it. Raising it to
        // make this go away is a product-rule change with no change-reason in this PR, and the CTO
        // forbade it explicitly (bind 2026-08-03 §2).
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            jan 2020 – dec 2024
            Ökade konverteringen med 23 procent.

            Utvecklare
            Beta AB
            2013 - 2021
            Byggde en ny betaltjänst.
            """;

        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(cv)),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        result.Verdicts.Single(v => v.CriterionId == "B6").Verdict
            .ShouldBe(CriterionVerdict.Warn,
                "month granularity beside year granularity IS two date formats — the old Pass came " +
                "from an entry the parser could not read, not from the CV being consistent.");
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

    [Theory]
    [InlineData("jan 2020 – dec 2024")]
    [InlineData("jan 2020 – nuvarande")]
    [InlineData("2020 – 2024 (heltid)")]
    [InlineData("2013 - 2021")]
    // "2020/01 – 2024/12" is deliberately ABSENT from this positive theory, and it stays absent
    // after round 5: DateRange no longer matches the slash point on either endpoint (decision D′),
    // so the segmenter stores no Period for this line at all — a stronger condition than "stored
    // but unparseable", and NotAssessed is the honest answer either way. The positive counterpart —
    // that it actually IS NotAssessed, not merely absent from this list — is
    // A4B6B7_AreNotAssessed_ForTheYearFirstSlashForm below.
    public async Task A4B6B7_AreAssessed_NotDegradedToNotAssessed(string dateLine)
    {
        // (S3) obligation 6 — the period-conditional criteria, measured as (S1) did for A1/A2/A6.
        // These are the criteria that flip to NotAssessed the moment a stored Period stops parsing,
        // and that flip is exactly what the first attempt at this widening caused on the asymmetric
        // class. The instrument already existed — this class runs the whole engine and the verdicts
        // were sitting in result.Verdicts unasserted.
        //
        // "2020 –" is deliberately absent: it stores no period at all (honest-absent, no end point),
        // so NotAssessed is the correct answer there and asserting otherwise would demand the engine
        // invent an end date.
        var result = await ReviewThreeLineLayoutAsync(dateLine);

        foreach (var id in new[] { "A4", "B6", "B7" })
        {
            result.Verdicts.Single(v => v.CriterionId == id).Verdict
                .ShouldNotBe(CriterionVerdict.NotAssessed,
                    $"{id} reads the period through PeriodParser. A period the segmenter extracted " +
                    "but the parser cannot read is not an honest 'not stated' — it is a period the " +
                    "CV states and the product drops.");
        }
    }

    [Fact]
    public async Task A4B6B7_AreNotAssessed_ForTheYearFirstSlashForm()
    {
        // THE POSITIVE COUNTERPART decision D′ owes: not merely "this row is absent from the
        // Assessed theory above" but "it is actually, positively NotAssessed" — a removed negative
        // claim with no added positive is unwatched behaviour (senior-cto-advisor round-5 bind §5).
        //
        // "2020/01 – 2024/12" stores no Period at all on the three-line layout (DateRange matches
        // neither slash endpoint), which is honest: the CV states a date the product declines to
        // read as one, so A4/B6/B7 report NotAssessed rather than inventing a span. This method
        // measures a DIFFERENT, independent axis of the same input — the structured Period field
        // A4/B6/B7 read, not the bullet-scorer path A1/A2/A6 read. The two axes disagreed with each
        // other until ADR 0136 (the row reached the bullet scorer while the Period was absent),
        // which is exactly why both keep a separate pin now that they agree.
        var result = await ReviewThreeLineLayoutAsync("2020/01 – 2024/12");

        foreach (var id in new[] { "A4", "B6", "B7" })
        {
            result.Verdicts.Single(v => v.CriterionId == id).Verdict
                .ShouldBe(CriterionVerdict.NotAssessed,
                    $"{id} reads the period through PeriodParser, and nothing was stored to read — " +
                    "the year-first SLASH form is unmodelled in both homes (decision D′).");
        }
    }

    [Fact]
    public async Task A4B6B7_AreNotAssessed_ForTheYearFirstSlashForm_OnTheFirstLineLayout()
    {
        // THE OTHER LAYOUT (obligation 8's second half). The date row IS Lines[0] here, so
        // ExtractPeriod's Year() fallback used to store the LEADING bare year ("2020"), which
        // parses — so A4/B6/B7 were ASSESSED, and this method asserted that as correct.
        //
        // IT WAS NOT CORRECT. "2020" parses to start==end, a span of ZERO years, for a CV stating
        // 2020/01 – 2024/12 — the confident wrong answer this lane refuses everywhere else. ADR 0136
        // suppresses the fallback for a date row the value grammar cannot read, so both layouts now
        // give the same honest NotAssessed. The layout split stays because the two reach it by
        // different paths, and a test that ran only one could not tell them apart.
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            2020/01 – 2024/12
            Acme AB
            Ökade konverteringen med 23 procent.
            """;

        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(cv)),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        foreach (var id in new[] { "A4", "B6", "B7" })
        {
            result.Verdicts.Single(v => v.CriterionId == id).Verdict
                .ShouldBe(CriterionVerdict.NotAssessed,
                    $"{id} reads the period through PeriodParser, and the veto stored nothing to " +
                    "read — a refusal, not a zero-length span claimed for a five-year tenure.");
        }
    }

    [Fact]
    public async Task B6_ShouldNotWarnMixedFormats_WhenOneEntryIsMonthNamedAndTheOtherIsSlashed()
    {
        // (S3) obligation 6, the two-entry case — and it needs a SECOND entry to fire, which is the
        // correction commit 1's own measurement taught this session: with one entry the defect shows
        // as a Pass carrying the wrong token, not as a Warn.
        //
        // THE FIXTURE MUST BE THE ASYMMETRIC FORM, and an earlier revision used the symmetric one.
        // With "jan 2020 – dec 2024" this test had ZERO kill power: that entry stored Period = null
        // before the widening, so it was EXCLUDED from the format set and B6 saw {MM/YYYY} → Pass;
        // after, it is {MM/YYYY} → Pass. Same verdict either way, while the docblock told a
        // measurement taken on a different row. A pin whose fixture cannot exhibit the defect is the
        // failure this whole PR is about, committed inside the pin written to prove the PR fixed it.
        //
        // MEASURED before the widening, on THIS fixture: "jan 2020 – nuvarande" stored
        // "2020 – nuvarande", whose start point is a bare year, so the token was YYYY. Beside a
        // slash-formatted sibling that is {YYYY, MM/YYYY} → "Blandade datumformat" on a CV that is
        // consistent at month granularity. That is the #420 class in a second notation — the same
        // defect commit 1 fixed for ISO — and it reddens on a revert.
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            jan 2020 – nuvarande
            Ökade konverteringen med 23 procent.

            Utvecklare
            Beta AB
            03/2019 – 05/2020
            Byggde en ny betaltjänst.
            """;

        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(cv)),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        result.Verdicts.Single(v => v.CriterionId == "B6").Verdict
            .ShouldBe(CriterionVerdict.Pass,
                "a month-named range and a slash range are ONE format at month granularity (#420).");
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
