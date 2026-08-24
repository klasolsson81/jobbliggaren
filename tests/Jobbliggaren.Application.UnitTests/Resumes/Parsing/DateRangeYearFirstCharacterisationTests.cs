using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
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

    // ── SLASH — a date ROW the LINE grammar reads and NO home dates (ADR 0136). ──

    [Theory]
    // SLASH x {start, end} x {NN INSIDE 01-12, NN OUTSIDE 01-12} — all four cells, both axes,
    // merged into ONE theory because decision D′ (senior-cto-advisor round-5 bind) made NN
    // validity irrelevant: no branch anywhere reads a slash NN as a month, so "2008/09" and
    // "2019/20" are the SAME grammatical case, and ADR 0136 kept it that way — the row grammar
    // recognises both or neither. Keeping them in separate
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
    // the slash — road 3 tried reading it as a month, on no cited authority, broke twice, and
    // took the reading back out. #1195 answered the LINE half alone and ADR 0136 owns that answer:
    // recognised as a date row by a SEPARATE row grammar, dated by nobody. The two axes below
    // therefore move independently, which is the whole point of the separation — the LINE axis is
    // TRUE on every row while the VALUE axis is byte-identical to what it was before ADR 0136.
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
    // END alternation's behaviour observable on its own, AND they are the two rows that prove
    // ADR 0136 did not reopen D′'s Blocker: both still store a value PeriodParser reads.
    [InlineData("2020 – 2024/12", "2020 – 2024")]
    [InlineData("2020-06 – 2024/12", "2020-06 – 2024")]
    public void SlashYearFirst_IsADateRow_AndIsStillDatedByNobody(
        string dateLine, string? expectedPeriod)
    {
        var (isDateOnly, period, parses) = NonFirstLine(dateLine);

        isDateOnly.ShouldBeTrue(
            "YYYY/NN is a date row on either endpoint, whatever NN is — false here hands it to the " +
            "Organization slot and releases it into the bullet scorer as prose (ADR 0136).");
        period.ShouldBe(expectedPeriod,
            $"[{dateLine}]: the VALUE grammar is untouched by ADR 0136, so a START-position slash " +
            "point still stores nothing (null) and an END-position one still degrades to whatever " +
            "the OTHER endpoint's own branch reaches — a bare year, a hyphen point's value, or " +
            "nothing, depending on that endpoint's own form — exactly as " +
            "the hyphen END cell degrades to a bare year " +
            "(HyphenEnd_InvalidMonth_DegradesToTheBareYear).");
        parses.ShouldBe(expectedPeriod is not null,
            "and whatever IS stored must be readable, which is the invariant the whole table serves " +
            "— the Blocker the mixed-notation rows exist to keep closed.");
    }

    [Theory]
    // THE FIRST-LINE LAYOUT'S TWIN, both NN populations — obligation 6 of the round-5 bind. Before
    // ADR 0136 both rows took the identical path: DateRange declined the START point, Year() fell
    // back to the LEADING bare year, and the trailing slash residue was never reached. That value
    // PARSED, to a span of start==end — 2019..2019 for a CV stating autumn 2019 to 2021, 2008..2008
    // for one stating a four-year läsår run. A4/B6/B7 assessed and Passed on it, and the deriver
    // attributed 0 years. This class pinned that as "a value both types agree on".
    //
    // It is not agreement, it is a CONFIDENT WRONG ANSWER, and ADR 0136's veto is what closes it:
    // an entry whose row grammar recognises a date row the value grammar cannot read gets no period
    // at all, which suppresses the Year() fallback along with everything else.
    [InlineData("2019/20 – 2021")]
    [InlineData("2008/09 – 2011/12")]
    public void SlashStart_OnTheFirstLineLayout_StoresNothingRatherThanAConfidentBareYear(string dateLine)
    {
        // The layout split, applied to the slash notation too. It reached one row of six in an
        // earlier revision while obligation 3 asked for it across the value axis — and this split
        // has already produced one wrong assertion in this lane, so the coverage is not decorative.
        var (_, period, parses) = FirstLine(dateLine);

        period.ShouldBeNull(
            "the veto suppresses Year()'s leading-year fallback: refusing beats a zero-length span " +
            "claimed for a multi-year tenure (ADR 0071, honest-absent; ADR 0136).");
        parses.ShouldBeFalse();
    }

    [Fact]
    public void SlashYearFirst_NoLongerUnshadowsAConfidentlyWrongSpanFromProse()
    {
        // THE UNSHADOWING DIRECTION (round-5 bind §2, "the consequence half" — obligation 4), NAMED
        // FOR WHAT IT COSTS. The date row's own slash pair does not match DateRange, so it cannot
        // consume the LEFTMOST match over the entry's whole text, and a range merely MENTIONED in a
        // later bullet became the one ExtractPeriod stored. Round 6 measured what that produced: the
        // CV states 2020/01 – 2024/12 (~5 years) and the engine reported a PARSEABLE, CONFIDENTLY
        // WRONG "2021 – 2023" (2 years) lifted out of a sentence about a budget she managed, not her
        // tenure. TryParseYearSpan succeeded on it, so A4/B6/B7 assessed and Passed rather than
        // reporting the honest NotAssessed a stored-nothing case gives.
        //
        // ADR 0136 closes it at the entry level, not at the match level: the row grammar recognises
        // the date row, the value grammar declines it, and ExtractPeriod refuses the whole entry
        // rather than answering from a bullet. The leftmost-match strategy is untouched.
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

        exp.Period.ShouldBeNull(
            "the entry states a period this engine may not date, so it states none — never one " +
            "lifted out of a prose bullet about something else.");
        PeriodParser.TryParseYearSpan(exp.Period, currentYear: 2026, out _, out _)
            .ShouldBeFalse("an honest NotAssessed, not a confident wrong answer (ADR 0071).");
    }

    [Fact]
    public void TheVetoIsEntryScoped_SoATrueDateRowBesideAnUnreadableOneIsAlsoLost()
    {
        // THE PATHOLOGICAL SHAPE, PRICED RATHER THAN GUARDED (senior-cto-advisor bind 2026-08-24).
        // An entry carrying BOTH an unreadable date row and a readable one loses both. Refusing is
        // the right direction under ADR 0071, and a rule that had to decide WHICH of two stated
        // periods is the entry's would be guessing — which is what this lane refuses to do. Pinned
        // so the cost is met rather than rediscovered.
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            2013 - 2021
            2020/01 – 2024/12
            """;

        var exp = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem();

        exp.Period.ShouldBeNull();

        // THE CONTRAST ROW the name owes: the same entry WITHOUT the unreadable row keeps its
        // period, so the veto is what removed it and not the layout.
        const string control = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            2013 - 2021
            """;

        new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(control).Content.Experience.ShouldHaveSingleItem()
            .Period.ShouldBe("2013 - 2021");
    }

    [Theory]
    // THE VETO'S OWN BOUNDARY, one row per conjunct of DatePatterns.IsUnreadableDateRow, each
    // crossing the threshold it names. Without these the veto could widen silently: every row here
    // is a line the row grammar touches and the veto must NOT fire on.
    //
    // RANGE conjunct — a line that is date-only only because Year() reduced it. "2020 –" reaches no
    // range branch, so it keeps origin/main's bare-year fallback rather than acquiring a refusal
    // this change was not written for.
    [InlineData("2020 –", "2020")]
    // date-ONLY conjunct — a line carrying a field beside the range. The suppression itself declines
    // this line, so the veto must too.
    //
    // THE SECOND ROW STORES A ZERO-LENGTH SPAN AND THIS THEORY NAMES IT RATHER THAN HIDING IT.
    // "Konsult 2020/01 – 2024/12" keeps Year()'s leading-year fallback, so the entry reports 2020
    // for a CV stating roughly five years — the same confident wrong answer the Lines[0] rows above
    // no longer give. It is origin/main's behaviour on a layout the veto deliberately does not
    // reach, and it is the FIFTH residual beside the four DatePatterns already prices. Asserting
    // ShouldNotBeNull here would pin the outcome without saying what it is, which is the
    // reverse-polarity pin this PR exists to remove.
    [InlineData("Konsult 2020/01 – 2024/12", "2020")]
    public void TheVetoDoesNotReachALineTheSuppressionItselfDeclines(
        string firstLine, string expectedPeriod)
    {
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            {firstLine}
            Acme AB
            {Bullet}
            """;

        var exp = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem();

        exp.Period.ShouldBe(expectedPeriod,
            $"[{firstLine}] is not an unreadable date row, so ExtractPeriod still answers — and " +
            "what it answers is asserted, not merely asserted to exist.");
    }

    [Theory]
    // THE LINE-GRAMMAR RISK #1195 NAMED AS ITS PRECONDITION — "a bracket-less trailing slash-year on
    // an organisation line" — measured and structurally empty: the row grammar adds a slash POINT
    // inside a RANGE alternation, and a bare trailing "2000/12" is not a range, so no branch reaches
    // it. Pinned rather than argued, because the precondition was set on this exact shape.
    [InlineData("Acme AB 2000/12")]
    [InlineData("Acme AB 2000-12")]
    public void ABareTrailingSlashYear_IsNotADateRow_AndTheOrganisationSurvives(string line)
    {
        DatePatterns.IsDateOnlyLine(line).ShouldBeFalse();
        DatePatterns.IsUnreadableDateRow(line).ShouldBeFalse();

        // The precondition was set on the CONSEQUENCE — that such a line must not lose its
        // employer — so the consequence is measured, not inferred from the predicates above.
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            {line}
            {Bullet}
            """;

        var exp = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem();

        exp.Organization.ShouldBe(line,
            "an organisation line carrying a trailing slash-year is still an organisation.");
    }

    [Theory]
    // R3 — StripDates reads the ROW grammar, so a slash range INSIDE a prose bullet is masked and
    // cannot read as a measurable digit (#487). Before ADR 0136 it read the VALUE grammar and left
    // "/01" and "/12" behind, which is the same §5 cited-evidence inversion the line-level
    // suppression closes, one altitude down. The negative control is the row that must stay TRUE.
    [InlineData("Ansvarig för perioden 2020/01 – 2024/12 av budgeten.", false)]
    [InlineData("Levererade 2020/01 – 2024/12 och ökade med 23 procent.", true)]
    [InlineData("Ökade konverteringen med 23 procent.", true)]
    // THE PRICED RESIDUAL: the row grammar's year class is unbounded (`\d{4}`, not `(?:19|20)\d{2}`),
    // so a bullet whose ONLY digits are NNNN/NN – NNNN masks whole and A1 stops seeing it.
    [InlineData("Hanterade 1500/25 – 4000 ärenden.", false)]
    // ITS DECLARED CONTROL, and it has no kill power BY DESIGN — false on origin/main too, because
    // `\d{4} – \d{4}` already ate it. That is what makes the row above a WIDENING of an accepted
    // class rather than a new one, measured here instead of argued in a report.
    [InlineData("Hanterade 1500 – 4000 ärenden.", false)]
    public void StripDates_MasksASlashRangeInsideProse(string bullet, bool expected)
    {
        // Asserted through the real consumer rather than on the mask string: the mask exists to
        // answer this question and nothing else reads it.
        ReviewText.ContainsMeasurableDigit(bullet).ShouldBe(expected);
    }

    // A CROSS-REFERENCE, not a row: the HYPHEN läsår collision (HyphenValidMonth_IsReadAsAMonth_WhichIsISO8601
    // above) is unaffected by decision D′. The hyphen form keeps reading these twelve start-years as
    // a month, on ISO 8601's authority. Written down so a reader who reaches the slash section
    // second is not left wondering whether the hyphen collision was also removed. It was not.

    [Fact]
    public void TheFourPointLists_AreATwoByTwoOverTwoOneTokenDeltas()
    {
        // (S5) constraint 4, extended to the 2x2 ADR 0136 created. The lists are
        // {START, END} x {value grammar, row grammar}, and they differ along exactly TWO orthogonal
        // axes, each one token wide:
        //
        //   HYPHEN axis (loose -> exact month class):  START -> END   and   LINE-START -> LINE-END
        //   SLASH  axis (absent -> present):           START -> LINE-START and END -> LINE-END
        //
        // Four byte-equalities over two substitutions cannot be satisfied by any other divergence,
        // which is what makes this a sync mechanism rather than a comment. Asserted as exact
        // substitutions rather than by splitting on '|' — the month class `(?:0[1-9]|1[0-2])`
        // contains a '|' of its own, so a naive token split compares lists of different lengths and
        // would either fail spuriously or, worse, be "fixed" by loosening the comparison until it
        // stopped measuring.
        const string looseHyphenPoint = @"|\d{4}-\d{2}|";
        const string exactHyphenPoint = @"|\d{4}-(?:0[1-9]|1[0-2])|";
        const string withoutSlashPoint = @"|\d{2}/\d{4}|\d{4})";
        const string withSlashPoint = @"|\d{2}/\d{4}|\d{4}/\d{2}|\d{4})";

        DatePatterns.StartPointForTests.Contains(looseHyphenPoint, StringComparison.Ordinal)
            .ShouldBeTrue("START keeps the loose year-first hyphen point — that is the hyphen delta.");
        DatePatterns.EndPointForTests.Contains(exactHyphenPoint, StringComparison.Ordinal)
            .ShouldBeTrue("END validates the month structurally, because that is where order bites.");
        DatePatterns.LineStartPointForTests.Contains(withSlashPoint, StringComparison.Ordinal)
            .ShouldBeTrue("only the ROW grammar carries the year-first slash point (ADR 0136).");
        DatePatterns.StartPointForTests.Contains(withSlashPoint, StringComparison.Ordinal)
            .ShouldBeFalse("the VALUE grammar must never carry it at THIS position. A slash point " +
                "placed elsewhere in the value lists passes every assertion here — the kill for " +
                "that lives on the characterisation theory's VALUE axis, not in this test.");

        // Replace is replace-ALL, so an equality alone would license "differs by any number of
        // occurrences of this substitution". Pinning each count makes each delta one token.
        (DatePatterns.StartPointForTests.Split(looseHyphenPoint).Length - 1).ShouldBe(1);
        (DatePatterns.LineStartPointForTests.Split(looseHyphenPoint).Length - 1).ShouldBe(1);
        (DatePatterns.StartPointForTests.Split(withoutSlashPoint).Length - 1).ShouldBe(1);
        (DatePatterns.EndPointForTests.Split(withoutSlashPoint).Length - 1).ShouldBe(1);

        DatePatterns.StartPointForTests.Replace(looseHyphenPoint, exactHyphenPoint, StringComparison.Ordinal)
            .ShouldBe(DatePatterns.EndPointForTests, "hyphen axis, value grammar.");
        DatePatterns.LineStartPointForTests.Replace(looseHyphenPoint, exactHyphenPoint, StringComparison.Ordinal)
            .ShouldBe(DatePatterns.LineEndPointForTests, "hyphen axis, row grammar — the SAME delta.");
        DatePatterns.StartPointForTests.Replace(withoutSlashPoint, withSlashPoint, StringComparison.Ordinal)
            .ShouldBe(DatePatterns.LineStartPointForTests, "slash axis, START position.");
        DatePatterns.EndPointForTests.Replace(withoutSlashPoint, withSlashPoint, StringComparison.Ordinal)
            .ShouldBe(DatePatterns.LineEndPointForTests,
                "slash axis, END position. Anything else diverging is a second grammar with nothing " +
                "keeping it in sync — the DRY objection that killed the masking-only alternative.");
    }
}
