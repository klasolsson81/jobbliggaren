using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1060 road 3, commit 1 — <c>DatePatterns.DateRange</c>'s point alternations are ordered
/// LONGEST-ALTERNATIVE-FIRST, and this class is that order's adjudicator.
///
/// <para><b>What the defect was.</b> .NET alternation is ordered and does not prefer the longest
/// branch: the first branch that lets the OVERALL match succeed wins. With the bare <c>\d{4}</c>
/// written before <c>\d{4}-\d{2}</c> in the END alternation, "2020-06 – 2024-03" matched only as
/// far as "2024" — the <c>\b</c> after it holds against the following "-", the match succeeds, and
/// nothing backtracks into the longer branch. The START alternation carried the same ordering and
/// never showed it, because there a too-short branch makes the overall match FAIL and the engine
/// backtracks. <b>Both are ordered now</b>, so the rule holds by construction rather than by which
/// side backtracking happens to rescue.</para>
///
/// <para><b>Why it is pinned on THREE surfaces and not one.</b> One token of ordering reached every
/// consumer that reads the match rather than the predicate. Pinning only
/// <see cref="DatePatterns.IsDateOnlyLine"/> would leave the two surfaces where the defect actually
/// costs the user — the stored <c>Period</c> value and the masking — free to regress alone. Each
/// method below names its own surface, and the fourth pins the downstream consequence end to end.
/// <b>Every assertion here was measured in both polarities</b>: the pre-correction values are
/// recorded in each test's comment, taken from a run against <c>b637b691</c>, not derived.</para>
///
/// <para><b>This class is a precondition, not a feature.</b> It carries no widening: the four forms
/// <c>DatePatterns</c> does not model ("jan 2020 – dec 2024", "2020 – 2024 (heltid)",
/// "2020/01 – 2024/12", "2020 –") are untouched by the ordering and stay on the negative side in
/// <c>DatePatternsDateOnlyLineTests</c>. It landed first so that every alternative added afterwards
/// goes into an ordered list — a "jan" branch before "januari" would match "januari 2020" as far as
/// "jan", which is the identical defect one alternative later (senior-cto-advisor re-bind
/// 2026-08-02, bind 9).</para>
/// </summary>
public class DatePatternsAlternationOrderingTests
{
    private const string IsoRange = "2020-06 – 2024-03";

    private static CvReviewEngine Engine() => new(
        RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
        AllCorrectSpellChecker(), RealAllowlist(),
        RealCvConventionsProvider(), RealParsingLexicon());

    // ── Surface 1: the raw match, which is what the other two read ───────────────────

    [Fact]
    public void DateRange_ShouldConsumeTheWholeIsoRange_NotJustItsYear()
    {
        // MEASURED before the correction: Value == "2020-06 – 2024", Length 14. The start point
        // was already whole ("2020-06") because a short START makes the overall match fail; only
        // the END was truncated.
        var match = DatePatterns.DateRange().Match(IsoRange);

        match.Success.ShouldBeTrue();
        match.Value.ShouldBe(IsoRange,
            "the end alternation must reach \\d{4}-\\d{2} before the bare \\d{4}, or the end month " +
            "is dropped while the overall match still succeeds.");
    }

    // ── Surface 2: IsDateOnlyLine — the predicate both engines share ─────────────────

    [Fact]
    public void IsDateOnlyLine_ShouldNowReachTheIsoRange_WhichWasTheFourthAxis()
    {
        // MEASURED before the correction: false, because the match stopped at "2024" and "-03"
        // stayed as a non-empty tail. This row used to sit in
        // DatePatternsDateOnlyLineTests.IsDateOnlyLine_ShouldBeFalse_WhereOnlyPeriodParserReachesTheForm
        // as the "ISO YYYY-MM end point" axis; the correction retires that axis, so the row MOVED
        // here rather than being edited in place — it is not a stale fixture, it is a fixture whose
        // subject changed sides.
        DatePatterns.IsDateOnlyLine(IsoRange).ShouldBeTrue();
        DatePatterns.StripTrailingDate(IsoRange).ShouldBeEmpty(
            "IsDateOnlyLine IS the reduction asked as a question — one home, not two copies.");

        // BOTH halves of ReviewText's union now reach this form. That is a strict widening of
        // suppression, never a substitution: the union still needs its PeriodParser half for the
        // axes DatePatterns does not reach (DatePatternsDateOnlyLineTests owns that list).
        PeriodParser.TryParse(IsoRange, out _, out _, out _).ShouldBeTrue();
    }

    // The NEIGHBOURING axis — a LONE ISO point "2020-06", which has no range separator and so
    // never reaches DateRange at all — is deliberately NOT re-asserted here. It is unchanged by the
    // ordering and already pinned in DatePatternsDateOnlyLineTests' PeriodParser-is-wider theory,
    // which is that list's one home. Bounding this correction's blast radius is a real obligation,
    // but discharging it by copying a row is how a list grows a second owner that can disagree.

    // ── Surface 3: the stored VALUE, on the path with no approve step ────────────────

    [Fact]
    public void ExtractPeriod_ShouldStoreTheWholeIsoRange_NotATruncatedOne()
    {
        // THE SURFACE THAT COSTS THE USER. ExtractPeriod returns the match VALUE, so the truncation
        // rode ParsedExperience.Period → RawPeriod into the promoted CV and into the rendered
        // document. MEASURED before the correction: Period == "2020-06 – 2024".
        //
        // On the AUTO-promote path there is no approve step, so the user never sees a diff that
        // would let her catch it (HeadingDrivenResumeSegmenter.StripTrailingPeriod names the
        // per-path difference). AutoPromoteContentMapper's "UNTRUNCATED" claim was true of the
        // mapper — it applies no length cap — and false of the pipeline, because the value arriving
        // was already short.
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Sjuksköterska, Region Skåne
            2020-06 – 2024-03
            Vårdade patienter.
            """;

        var result = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load()).Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Period.ShouldBe(IsoRange, "the end month must survive into the stored period.");
    }

    // ── Surface 4: StripDates masking, read by ContainsMeasurableDigit ───────────────

    [Fact]
    public void StripDates_ShouldLeaveNoDigitBehind_ForAnInlineIsoRange()
    {
        // MEASURED before the correction: the "-03" survived the mask, so a prose bullet carrying
        // an inline ISO range read as carrying a measurable digit — and A1 counts exactly that as a
        // quantified result, which is the class #487 exists to prevent.
        //
        // Asserted through the REAL consumer as well as on the mask itself: StripDates has one
        // production reader (ReviewText.ContainsMeasurableDigit), and a mask assertion alone would
        // pin the intermediate value while leaving the question the intermediate value exists to
        // answer unmeasured.
        const string bullet = "Anställd 2020-06 – 2024-03 som konsult";

        DatePatterns.StripDates(bullet).Any(char.IsDigit).ShouldBeFalse(
            "an unmasked end month is a digit the measurable-result test would count.");
        ReviewText.ContainsMeasurableDigit(bullet).ShouldBeFalse(
            "employment dates are not a quantified result, whichever notation they are written in.");
    }

    // ── The downstream consequence, end to end ───────────────────────────────────────

    [Fact]
    public async Task B6_ShouldNotWarnMixedFormats_WhenBothEntriesAreMonthGranular()
    {
        // THE DEFECT #420 EXISTS TO PREVENT, reproduced by the ordering one layer up. The truncated
        // "2020-06 – 2024" parses as MIXED granularity (month start, year end), so PeriodParser
        // degrades its format token to "YYYY"; beside a slash-formatted entry's "MM/YYYY" that is
        // two distinct tokens.
        //
        // MEASURED before the correction: Warn, "Blandade datumformat: YYYY, MM/YYYY." — on a CV
        // that is consistent at month granularity throughout. PeriodParser deliberately maps ISO
        // and slash notation to the SAME "MM/YYYY" token precisely so this cannot happen (#420);
        // the ordering defeated that upstream of it.
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            2020-06 – 2024-03
            Ökade konverteringen med 23 procent.

            Utvecklare
            Beta AB
            03/2019 – 05/2020
            Byggde en ny betaltjänst.
            """;

        var parsed = ResumeFromCvText(cv);
        var result = await Engine().ReviewAsync(
            CvReviewContext.FromParsed(parsed), RenderProfile.Ats, TestContext.Current.CancellationToken);

        var b6 = result.Verdicts.First(v => v.CriterionId == "B6");
        b6.Verdict.ShouldBe(CriterionVerdict.Pass,
            "an ISO month range and a slash month range are ONE format at month granularity (#420).");
    }
}
