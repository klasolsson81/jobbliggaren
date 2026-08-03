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
/// that decays (CLAUDE.md §9.6 filing discipline). The <c>DateRange</c> baselines were RUN on
/// 2026-08-03, by substituting <c>b637b691</c>'s <c>DatePatterns.cs</c> and re-running these rows.
/// The one claim about <c>origin/main</c>'s <c>PeriodParser</c> — that <c>YYYY/MM</c> was modelled in
/// NEITHER home — is READ from that file's <c>PointRegex</c> (its year-first branch is
/// hyphen-only), because the substitution above leaves <c>PeriodParser</c> at HEAD and so cannot
/// measure it. Said separately rather than folded in: one provenance sentence covering two different
/// instruments is how a read becomes reported as a run.</para>
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

    [Theory]
    // THE β-3 AXIS, and it is the argument the restore was made on — so it gets a row rather than a
    // sentence. Neither layout above is the one where β-3 bites: on the TWO-LINE "Title / Dates"
    // layout the date row is Lines[1] and therefore the ORGANISATION candidate, and
    // SplitTitleOrganization nulls the slot only when IsDateOnlyLine says the line carries no field.
    //
    // At commit 4 the academic-year form stopped being a date row, so this returned "2019-20 – 2021"
    // as the employer — a fabricated organization on a CV the user sends to employers, which is the
    // exact class #1060 β-3 exists to close. Measured here rather than argued.
    [InlineData("2019-20 – 2021")]
    [InlineData("2019-20 – nuvarande")]
    [InlineData("2009-10 – 2021")]
    public void TheDateRowNeverBecomesTheOrganisation_OnTheTwoLineLayout(string dateLine)
    {
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            {dateLine}
            {Bullet}
            """;

        var exp = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem();

        exp.Title.ShouldBe("Systemutvecklare");
        exp.Organization.ShouldBeNull(
            "a line carrying nothing but a date must not become the employer (#1060 β-3). This is " +
            "the axis the restore was justified on, so it is asserted rather than reasoned about.");
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
        var (isDateOnly, period, parses) = NonFirstLine(dateLine);

        isDateOnly.ShouldBeTrue();
        period.ShouldNotBeNull("the value axis is asserted on every row, not only the line axis.");
        parses.ShouldBeTrue("ISO 8601 says these two digits are a month, and both homes agree.");
    }

    // ── SLASH — modelled by NEITHER home. See the docblock note and round 5's decision D′. ──

    [Theory]
    // SLASH x {start, end} x {NN INSIDE 01-12, NN OUTSIDE 01-12} — all four cells, both axes,
    // merged into ONE theory because decision D′ (senior-cto-advisor round-5 bind) made NN
    // validity irrelevant: the branch that once read a valid NN as a month is gone from BOTH point
    // lists, so "2008/09" and "2019/20" are now the SAME grammatical case. Keeping them in separate
    // theories after that merge would itself be the lane's signature failure one altitude up — an
    // instrument indexed by a distinction the grammar no longer draws.
    //
    // ATTRIBUTION, measured per commit, kept because it is the record of how this collided: the
    // SLASH half arrived in commit 2 (DatePatterns) and commit 3 (PeriodParser). origin/main
    // modelled it in NEITHER home. It briefly (commits 2-8) read a VALID NN as a month in
    // DatePatterns — the Klas-direktiv commit narrowed that to PeriodParser only, and round 5
    // removed it from DatePatterns too, after a Blocker showed the split itself was unsafe (a
    // mixed-notation range stored an unreadable value — see
    // DateModelWideningStoredPeriodTests.WhatTheSegmenterStores_ThePeriodParserCanRead_ForEveryModelledForm).
    //
    // THE NOTATION-AUTHORITY ASYMMETRY IS STILL THE POINT, even though the collision itself
    // dissolved. ISO 8601 adjudicates the hyphen (see HyphenValidMonth below). Nothing adjudicates
    // the slash — this PR tried reading it as a month, on no cited authority, broke twice, and
    // took the reading back out. Whether the LINE half alone (recognise, still never date) should
    // return is a product question, tracked in **#1195**.
    [InlineData("2019/20 – 2021", null)]
    [InlineData("2018 – 2019/20", "2018 – 2019")]
    [InlineData("2008/09 – 2011/12", null)]
    [InlineData("2000/01 – 2011/12", null)]
    // THE PR's OWN NAMED-DEFECT ROW, direct — every other test that uses this exact string asserts
    // something ELSE about it (the unshadowing consequence, the two-line Organization fabrication,
    // the review-engine verdict), so none of them is a direct IsDateOnlyLine/Period pin in isolation.
    // This row is that pin.
    [InlineData("2020/01 – 2024/12", null)]
    // THE MISSING CELL an earlier revision of this table did not have: {slash, END, NN ∈ 01-12}
    // with a NON-slash START — the exact shape of the Blocker that triggered decision D′. The two
    // existing valid-NN rows above carry slash at BOTH endpoints, so neither can see this cell: a
    // START that also fails to match masks whatever the END alternation does. These two make the
    // END alternation's behaviour observable on its own, and they are the two rows a round-5
    // commit deleted from DateModelWideningStoredPeriodTests under a false claim that they lived
    // here — restored in both places now.
    [InlineData("2020 – 2024/12", "2020 – 2024")]
    [InlineData("2020-06 – 2024/12", "2020-06 – 2024")]
    public void SlashYearFirst_IsNeverModelled_RegardlessOfMonthValidity(
        string dateLine, string? expectedPeriod)
    {
        var (isDateOnly, period, parses) = NonFirstLine(dateLine);

        isDateOnly.ShouldBeFalse(
            "YYYY/NN is modelled by no branch, on either endpoint, whatever NN is — decision D′ made " +
            "this uniform across the whole grammar.");
        period.ShouldBe(expectedPeriod,
            $"[{dateLine}]: a START-position slash point stores nothing (null); an END-position " +
            "one degrades to whatever the OTHER endpoint's own branch reaches — a bare year, a " +
            "hyphen point's value, or nothing, depending on that endpoint's own form — exactly as " +
            "the hyphen END cell degrades to a bare year " +
            "(HyphenEnd_InvalidMonth_DegradesToTheBareYear).");
        parses.ShouldBe(expectedPeriod is not null,
            "and whatever IS stored must be readable, which is the invariant the whole table serves " +
            "— the Blocker the mixed-notation rows exist to keep closed.");
    }

    [Theory]
    // THE FIRST-LINE LAYOUT'S TWIN, both NN populations — obligation 6 of the round-5 bind: the
    // valid-NN row is no longer vacuous under decision D′ (it used to be masked entirely, because
    // DateRange matched the whole line and the Year() fallback never ran). Now both rows take the
    // identical path: DateRange declines the START point, so Year() falls back to the LEADING bare
    // year and the trailing slash residue is simply never reached.
    [InlineData("2019/20 – 2021", "2019")]
    [InlineData("2008/09 – 2011/12", "2008")]
    public void SlashStart_OnTheFirstLineLayout_StoresTheBareYear(string dateLine, string expectedPeriod)
    {
        // The layout split, applied to the slash notation too. It reached one row of six in an
        // earlier revision while obligation 3 asked for it across the value axis — and this split
        // has already produced one wrong assertion in this PR, so the coverage is not decorative.
        var (_, period, parses) = FirstLine(dateLine);

        period.ShouldBe(expectedPeriod, "Year()'s fallback takes the leading year when DateRange declines.");
        parses.ShouldBeTrue();
    }

    [Fact]
    public void SlashYearFirst_UnshadowsAConfidentlyWrongSpanFromProse_KnownOriginMainRisk()
    {
        // THE UNSHADOWING DIRECTION (round-5 bind §2, "the consequence half" — obligation 4), NAMED
        // FOR WHAT IT COSTS, NOT FOR THE FACT THAT IT REPRODUCES origin/main. Once the date row's
        // own slash pair stops matching DateRange, it can no longer consume the LEFTMOST match over
        // the entry's whole text — so a range merely MENTIONED in a later bullet becomes the one
        // ExtractPeriod stores as the entry's Period. That is `origin/main`'s own behaviour (it never
        // modelled the slash form either, so this fall-through always existed for this row) — but
        // "not new" is not "safe", and round 6 measured what it actually produces: the CV states
        // 2020/01 – 2024/12 (~5 years) and the engine reports a PARSEABLE, CONFIDENTLY WRONG
        // "2021 – 2023" (2 years) lifted out of a sentence about a budget she managed, not her
        // tenure. TryParseYearSpan succeeds on it, so A4/B6/B7 assess and Pass rather than reporting
        // the honest NotAssessed a stored-nothing case would — the "confident wrong answer is worse
        // than a refusal" position this lane holds everywhere else, silently defeated here. Tracked
        // as an additional named consequence in #1195; not fixed here (decision D′ did not create
        // it, and fixing it is a change to ExtractPeriod's leftmost-match strategy, a separate
        // change-reason).
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            2020/01 – 2024/12
            Ansvarig för perioden 2021 – 2023 av budgeten.
            """;

        var exp = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem();

        exp.Period.ShouldBe("2021 – 2023",
            "with the date row's own text unmatched, DateRange's leftmost scan over the whole entry " +
            "finds the range mentioned in the bullet instead — origin/main's behaviour, not a new one.");
        PeriodParser.TryParseYearSpan(exp.Period, currentYear: 2026, out var start, out var end)
            .ShouldBeTrue("the wrong span PARSES, which is the sharp part: it is not an honest " +
                "NotAssessed, it is a confident wrong answer.");
        start.ShouldBe(2021);
        end.ShouldBe(2023, "not the ~5 years the CV's own date row states.");
    }

    // A CROSS-REFERENCE, not a row: the HYPHEN läsår collision (HyphenValidMonth_IsReadAsAMonth_WhichIsISO8601
    // above) is unaffected by decision D′. The hyphen form keeps reading these twelve start-years as
    // a month, on ISO 8601's authority. Written down so a reader who reaches the slash section
    // second is not left wondering whether the hyphen collision was also removed. It was not.

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

        // Replace is replace-ALL, so the equality alone would license "differs by any number of
        // occurrences of this substitution". Pinning the count makes it "exactly one token".
        var occurrences = DatePatterns.StartPointForTests.Split(looseHyphenPoint).Length - 1;
        occurrences.ShouldBe(1, "the loose branch appears once, so the delta is one token and not a class.");

        DatePatterns.StartPointForTests.Replace(looseHyphenPoint, exactHyphenPoint, StringComparison.Ordinal)
            .ShouldBe(DatePatterns.EndPointForTests,
                "the two point lists must be byte-identical apart from the year-first hyphen month " +
                "class. Anything else diverging is a second grammar with nothing keeping it in sync.");
    }
}
