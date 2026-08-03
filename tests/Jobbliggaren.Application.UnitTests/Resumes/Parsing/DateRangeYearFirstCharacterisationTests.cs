using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1060 road 3 — the year-first grammar characterised by ITS OWN AXES, not by any finding
/// ((S5) obligation 3, senior-cto-advisor R3 bind 2026-08-03).
///
/// <para><b>This class exists because three review rounds found the same defect and every round's
/// instrument was indexed by the finding that prompted it.</b> Commit 1 moved the end reading and
/// moved the academic-year population with it; commit 4 moved the structural month class and moved
/// the start orientation with it. Each round's acceptance obligations discharged LITERALLY while an
/// unpinned behaviour moved — the worked case is "pin that nothing else moved", satisfied by a grep
/// over fixtures, when the behaviour space is not the fixture space. The diff's only start-position
/// academic-year row was the SLASH variant: the one notation where nothing could move.</para>
///
/// <para><b>So this table is indexed by the grammar: {hyphen, slash} × {start, end} × {NN inside
/// 01-12, NN outside}.</b> Every row asserts BOTH axes — the LINE question
/// (<see cref="DatePatterns.IsDateOnlyLine"/>, which drives β-3 suppression) and the VALUE question
/// (the stored <c>Period</c> and whether <see cref="PeriodParser"/> reads it) — because those two
/// have moved independently in this PR and once in opposite directions. A change to
/// <see cref="DatePatterns.DateRange"/> that moves any cell here is visible whether or not anyone
/// thought to write a fixture for it. It is a fitness function for the one characteristic this lane
/// kept breaking.</para>
///
/// <para><b>The <c>origin/main</c> baselines live in comments with the date they were measured, not
/// as assertions</b> — a test cannot run a tree it is not on, and an undated baseline is a claim
/// that decays (CLAUDE.md §9.6 filing discipline). Measured 2026-08-03 by checking out
/// <c>b637b691</c>'s <c>DatePatterns.cs</c> and running the same rows.</para>
/// </summary>
public class DateRangeYearFirstCharacterisationTests
{
    private const string Bullet = "Ökade konverteringen med 23 procent.";

    // The date row as Lines[2] — the three-line "Title / Company / Dates" layout.
    private static (bool IsDateOnly, string? Period, bool Parses) NonFirstLine(string dateLine)
    {
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            {dateLine}
            {Bullet}
            """;
        var exp = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem();
        return (DatePatterns.IsDateOnlyLine(dateLine), exp.Period,
            PeriodParser.TryParse(exp.Period, out _, out _, out _));
    }

    // The date row as Lines[0] — the layout that reaches ExtractPeriod's Year() fallback. Split out
    // because this split has already bitten once: a value measured on one layout was asserted of the
    // other, and the theory went green for the wrong reason until the run refused it.
    private static (bool IsDateOnly, string? Period, bool Parses) FirstLine(string dateLine)
    {
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            {dateLine}
            Acme AB
            {Bullet}
            """;
        var exp = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem();
        return (DatePatterns.IsDateOnlyLine(dateLine), exp.Period,
            PeriodParser.TryParse(exp.Period, out _, out _, out _));
    }

    // ── HYPHEN, NN OUTSIDE 01-12 — the academic year. The population R12 moved. ──────
    //
    // origin/main (measured 2026-08-03): START "2019-20 – 2021" matched whole → IsDateOnlyLine TRUE,
    // stored whole, PeriodParser refused (honest NotAssessed). END "2018 – 2019-20" matched
    // "2018 – 2019" → IsDateOnlyLine FALSE, stored "2018 – 2019", parsed.
    //
    // Commit 4 narrowed BOTH alternations and the start position lost origin/main's backtracking
    // rescue: no match at all, so IsDateOnlyLine went TRUE → FALSE (β-3 re-opened) and on the
    // Lines[0] layout the honest refusal became a confident "2019". The restore puts the loose
    // branch back in START only.

    [Fact]
    public void HyphenStart_InvalidMonth_IsStillADateRow_AndStillHonestlyRefused()
    {
        var (isDateOnly, period, parses) = NonFirstLine("2019-20 – 2021");

        isDateOnly.ShouldBeTrue(
            "an academic year is still a date row; false here hands it to the Organization slot.");
        period.ShouldBe("2019-20 – 2021", "the stored value stays source-faithful.");
        parses.ShouldBeFalse(
            "and PeriodParser still refuses it — an honest NotAssessed, which is origin/main's " +
            "answer and better than a confident wrong span.");
    }

    [Fact]
    public void HyphenStart_InvalidMonth_OnTheFirstLineLayout_DoesNotBecomeAConfidentYear()
    {
        // The sharper half of R12: at commit 4 this stored "2019" and PARSED — a one-year period
        // claimed for a row that says autumn-2019 to 2021. A confident wrong answer where
        // origin/main refused.
        var (_, period, parses) = FirstLine("2019-20 – 2021");

        period.ShouldBe("2019-20 – 2021");
        parses.ShouldBeFalse("refusing beats inventing a span (ADR 0071, honest-absent).");
    }

    [Fact]
    public void HyphenEnd_InvalidMonth_DegradesToTheBareYear()
    {
        // The END alternation keeps the exact month class, which is where prefix-order bites: the
        // short \d{4} can complete the match there, so an inexact class lets the wrong branch win.
        // This is R1's repair and it stays.
        var (isDateOnly, period, parses) = NonFirstLine("2018 – 2019-20");

        isDateOnly.ShouldBeFalse("origin/main behaviour: the orphan '-20' keeps the line non-empty.");
        period.ShouldBe("2018 – 2019");
        parses.ShouldBeTrue("the bare-year reading is one both types agree on.");
    }

    // ── HYPHEN, NN INSIDE 01-12 — the läsår collision. NOT this PR's, see below. ─────

    [Theory]
    // MEASURED PER COMMIT (2026-08-03), and the attribution matters because it was got wrong once:
    //   "2018 – 2019-10"  origin/main "2018 – 2019"  → commit 1 made it match whole (the END reading)
    //   "2009-10 – 2021"  origin/main matched whole  → unchanged by every commit in this PR
    // A character-class NARROWING yields a subset language, so commit 4 cannot have added either.
    // ISO 8601 adjudicates the hyphen: 2009-10 IS October 2009 by a written standard, and the app
    // read it that way before this PR. The collision with a läsår is real and is not ours to resolve.
    [InlineData("2009-10 – 2021")]
    [InlineData("2018 – 2019-10")]
    public void HyphenValidMonth_IsReadAsAMonth_WhichIsISO8601(string dateLine)
    {
        var (isDateOnly, _, parses) = NonFirstLine(dateLine);

        isDateOnly.ShouldBeTrue();
        parses.ShouldBeTrue("ISO 8601 says these two digits are a month, and both homes agree.");
    }

    // ── SLASH — modelled by NEITHER home before this PR. See the docblock note. ──────

    [Fact]
    public void SlashStart_InvalidMonth_IsNotModelled_AsOnMain()
    {
        // origin/main: no match (YYYY/MM was modelled nowhere). Commit 2 grew it; commit 4's exact
        // class returned this cell to no-match. Unchanged by the restore, which touches the hyphen
        // branch only.
        var (isDateOnly, _, _) = NonFirstLine("2019/20 – 2021");

        isDateOnly.ShouldBeFalse("YYYY/NN with NN outside 01-12 is modelled by no branch.");
    }

    [Theory]
    // THE LÄSÅR COLLISION, PINNED AS A KNOWN INSTANCE — not as a defect, and not as ours to fix.
    //
    // A Swedish läsår is YYYY/YY where YY = (YYYY+1) mod 100, which lands INSIDE 01-12 for exactly
    // twelve start-years: 2000/01 through 2011/12. So this branch reads "2008/09" as September 2008.
    //
    // ATTRIBUTION, measured per commit: the SLASH half arrived in commit 2, which introduced
    // YYYY/MM in DatePatterns (commit 3 taught PeriodParser the same). origin/main modelled it in
    // NEITHER home. Commit 4 could not have created it — a narrowing cannot add a match.
    //
    // THE NOTATION-AUTHORITY ASYMMETRY IS THE POINT. ISO 8601 adjudicates the hyphen. Nothing
    // adjudicates the slash: this PR picked a reading for the one notation Swedish convention gives
    // the academic and fiscal year, with no cited standard on either side. That is a product belief,
    // and it is Klas's to state — see the PR body. Pinned here so the reading is visible rather than
    // implicit, and so a later change to it is a change to a pinned behaviour.
    [InlineData("2008/09 – 2011/12")]
    [InlineData("2000/01 – 2011/12")]
    public void SlashValidMonth_IsReadAsAMonth_AndNothingButConventionSaysOtherwise(string dateLine)
    {
        var (isDateOnly, _, parses) = NonFirstLine(dateLine);

        isDateOnly.ShouldBeTrue();
        parses.ShouldBeTrue(
            "the widening reads YYYY/NN as year-and-month. For NN in 01-12 that collides with a " +
            "Swedish läsår, and no standard decides the slash form — see the PR body's question.");
    }

    [Fact]
    public void TheTwoPointLists_DifferByExactlyOneToken()
    {
        // (S5) constraint 4 — the divergence between the START and END point lists is the contract,
        // so it is asserted rather than trusted. Anything else diverging is a silent second grammar,
        // which is the DRY objection that killed the "masking-only pattern" alternative.
        // Asserted as an exact substitution rather than by splitting on '|' — the month class
        // `(?:0[1-9]|1[0-2])` contains a '|' of its own, so a naive token split compares lists of
        // different lengths and would either fail spuriously or, worse, be "fixed" by loosening the
        // comparison until it stopped measuring. Substituting the one permitted token and demanding
        // byte equality cannot be satisfied by any other divergence.
        const string looseHyphenPoint = @"|\d{4}-\d{2}|";
        const string exactHyphenPoint = @"|\d{4}-(?:0[1-9]|1[0-2])|";

        DatePatterns.StartPointForTests.Contains(looseHyphenPoint, StringComparison.Ordinal)
            .ShouldBeTrue("START keeps the loose year-first hyphen point — that is the whole delta.");
        DatePatterns.EndPointForTests.Contains(exactHyphenPoint, StringComparison.Ordinal)
            .ShouldBeTrue("END validates the month structurally, because that is where order bites.");

        DatePatterns.StartPointForTests.Replace(looseHyphenPoint, exactHyphenPoint, StringComparison.Ordinal)
            .ShouldBe(DatePatterns.EndPointForTests,
                "the two point lists must be byte-identical apart from the year-first hyphen month " +
                "class. Anything else diverging is a second grammar with nothing keeping it in sync.");
    }
}
