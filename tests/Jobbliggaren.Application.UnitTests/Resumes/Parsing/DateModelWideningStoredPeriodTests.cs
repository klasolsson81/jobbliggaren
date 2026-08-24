using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1060 road 3 — the relationship <c>DatePatterns</c>' docblock has always claimed and nothing ever
/// tested: <b>what <c>ExtractPeriod</c> stores, <c>PeriodParser</c> should be able to read</b>
/// ((S3) obligations 1, 2 and 5, (S4) 1 and 5; senior-cto-advisor binds 2026-08-03).
///
/// <para><b>IT IS NOT UNIVERSAL, AND AN EARLIER REVISION OF THIS FILE SAID IT WAS.</b> The honest
/// statement is a characterisation, not a quantifier: <i>the segmenter stores nothing the parser
/// refuses, EXCEPT where <c>DateRange</c> validates a component STRUCTURALLY and
/// <c>PeriodParser</c> validates it SEMANTICALLY.</i> That gap is the two types' deliberate division
/// of labour — <c>DateRange</c> answers "does this look like a date", <c>PeriodParser</c> answers "is
/// this a date" — and it is why the second exists.
/// <b>KNOWN INSTANCES, NOT AN EXHAUSTIVE COUNT, and no total is published here on purpose</b>: the
/// month (<c>13/2020 – 2024</c>, a documented axis with its own frozen pin) and the year
/// (<c>1500 – 2000</c>, where <c>DateRange</c> takes any <c>\d{4}</c> and <c>PeriodParser</c>
/// enforces 1900–2100). Both are pre-existing, neither is a regression, and both are run rather than
/// read. The lane has had a count wrong twice; this file will not publish a third.</para>
///
/// <para><b>This class exists because the invariant was broken by the commit that stated it.</b> The
/// widening first landed with month names and <c>YYYY/MM</c> added to <see cref="DatePatterns"/>
/// alone. Measured in both polarities: <c>"jan 2020 – nuvarande"</c> stored
/// <c>Period = "2020 – nuvarande"</c> before — parseable, years attributed 2020..2026 — and
/// <c>"jan 2020 – nuvarande"</c> after, which <c>PeriodParser</c> refused. Every pin the change
/// shipped with was blind to it, because all of them used the four forms the change was written for,
/// and every one of those had a <c>Period</c> that was already null. The regressing population was
/// the ASYMMETRIC one — a widened point on one side, an already-modelled point or a present-keyword
/// on the other — and it sat outside the frozen fixture set by construction.</para>
///
/// <para><b>The rows below are the probe's rows, kept.</b> A measurement that dies with its throwaway
/// probe is a measurement nobody can re-run; these are the same inputs, promoted to a permanent
/// theory so the invariant has an adjudicator rather than a docblock.</para>
/// </summary>
public class DateModelWideningStoredPeriodTests
{
    private static string? PeriodFor(string dateLine)
    {
        // Through the REAL segmenter, never a hand-built Period (CLAUDE.md §5, Tests) — the whole
        // point is that ExtractPeriod's own output must survive PeriodParser.
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            {dateLine}
            Ökade konverteringen med 23 procent.
            """;

        return new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem().Period;
    }

    [Theory]
    // THE ASYMMETRIC CLASS — the population that regressed. Each had a parseable Period before the
    // widening and must have one after.
    [InlineData("jan 2020 – nuvarande", "MM/YYYY")]
    [InlineData("mars 2021 – pågående", "MM/YYYY")]
    [InlineData("jan 2020 – 2024", "YYYY")]
    // A SECOND, DISTINCT asymmetric row — a modelled point (bare year / ISO hyphen) paired with the
    // year-first SLASH point, which stores whole (DateRange still matched it) and then went from
    // parsing under commit 3 to REFUSED under the Klas-direktiv commit — a strict regression against
    // origin/main, where the bare-\d{4} fallback degraded these to a working "YYYY" reading. This is
    // the row that broke round 5's mandatory review, and it was deleted from this theory in the same
    // commit that broke it, under a claim that was false — see the correction below.
    //
    // ROUND 5 CORRECTION (senior-cto-advisor bind, decision D′). The removed comment here read: "The
    // year-first SLASH rows moved OUT of this theory … they live in DateRangeYearFirstCharacterisationTests
    // where the whole year-first grammar is indexed." That was checked and is FALSE: neither string
    // below exists anywhere in that file, which carries only the PURE both-slash and both-hyphen
    // pairs, never this mixed-notation shape. What actually happened: the rows were deleted to keep
    // the theory green against a regression nobody had measured yet. Decision D′ removed the
    // year-first SLASH point from DateRange entirely (both endpoints, both point lists), so these two
    // rows are back to origin/main behaviour: the slash tail is dropped by the bare-\d{4} fallback,
    // never stored, and the token is the COARSER one on a mixed-granularity range (PeriodParser.cs's
    // TryParse: "the coarser token wins so B6 flags the inconsistency at the entry level").
    [InlineData("2020 – 2024/12", "YYYY")]
    [InlineData("2020-06 – 2024/12", "YYYY")]
    // THE SYMMETRIC CLASS — both endpoints widened. These had NO period at all before; they are
    // here because the property under test is about what the segmenter STORES, not about what
    // regressed. (An earlier revision said "the invariant is universal over what the segmenter
    // stores". It is not — see the characterisation in this class's docblock, and the two
    // structural-vs-semantic instances pinned below.)
    [InlineData("jan 2020 – dec 2024", "MM/YYYY")]
    // "2020/01 – 2024/12" — BOTH endpoints year-first SLASH — is NOT a row here, and that is
    // decision D′ plus ADR 0136, not an oversight: DateRange still matches neither slash endpoint,
    // and ADR 0136's veto now makes the segmenter store nothing on EVERY layout rather than only
    // this one (PeriodFor returns null). That is a different mechanism from the mixed rows above,
    // which still store a truncated-but-readable value — a pure-slash pair has no modelled point on
    // either side to fall back to. Pinned as a stored-nothing case in
    // DateRangeYearFirstCharacterisationTests, which owns the whole year-first grammar; this theory
    // is about what IS stored, so an input that stores nothing does not belong in it.
    // Controls the widening must not disturb.
    [InlineData("2013 - 2021", "YYYY")]
    [InlineData("2020-06 – 2024-03", "MM/YYYY")]
    [InlineData("2020 – 2024 (heltid)", "YYYY")]
    public void WhatTheSegmenterStores_ThePeriodParserCanRead_ForEveryModelledForm(string dateLine, string expectedToken)
    {
        var period = PeriodFor(dateLine);

        period.ShouldNotBeNull($"the segmenter must recover a period from [{dateLine}].");
        PeriodParser.TryParse(period, out _, out _, out var token).ShouldBeTrue(
            $"[{period}] is what ExtractPeriod STORED. A value the segmenter can extract and this " +
            "parser refuses is not an honest 'not stated' — it is a period the CV states and the " +
            "product drops, costing A4/B6/B7 their verdicts and the deriver its years.");
        token.ShouldBe(expectedToken,
            "the granularity token drives B6; a month-name point is month granularity like any other.");
    }

    [Theory]
    // (S4) obligation 1 — THE ACADEMIC / FISCAL YEAR, and the reason DateRange validates the month
    // structurally in its year-first branches.
    //
    // "2019/20" and "2019-20" are how a Swedish CV writes a läsår or a räkenskapsår. An earlier
    // revision said the last two digits therefore lie outside 01-12 "BY CONSTRUCTION". THEY DO
    // NOT: a läsår is YYYY/YY where YY = (YYYY+1) mod 100, which lands INSIDE 01-12 for twelve
    // start-years, 2000/01 through 2011/12. For the HYPHEN form those twelve are read as months (ISO
    // 8601 adjudicates it) and are pinned as a known collision in DateRangeYearFirstCharacterisationTests.
    // The SLASH form no longer reads any NN as a month, valid or not (decision D′, round 5) — see
    // that same file's merged theory and #1195. The rows below are the OTHER half — NN outside
    // 01-12, on BOTH notations — where with a bare \d{2} the month-bearing branch won in the END
    // alternation and the whole line was stored, then refused by PeriodParser, costing A4/B6/B7
    // their verdicts and the deriver its years.
    //
    // MEASURED in both polarities. Before this PR: stored "2018 – 2019", parsed. At the widening's
    // second commit: stored whole, REFUSED. With the structural month class: back to "2018 – 2019",
    // parses. That is repair to origin/main behaviour, not improvement on it — see the two residuals
    // pinned below, which are unchanged and stay open.
    [InlineData("2018 – 2019-20", "2018 – 2019")]
    [InlineData("2018 – 2019/20", "2018 – 2019")]
    [InlineData("2020 – 2024/25", "2020 – 2024")]
    public void AnAcademicYearDegradesToTheBareYear_RatherThanBeingStoredAndRefused(
        string dateLine, string expectedPeriod)
    {
        var period = PeriodFor(dateLine);

        period.ShouldBe(expectedPeriod,
            "the month-bearing branch must decline a token that is not a month, so the bare-year " +
            "reading wins and the stored value is one both types agree on.");
        PeriodParser.TryParse(period, out _, out _, out _).ShouldBeTrue(
            $"[{period}] is what ExtractPeriod stored; refusing it loses the entry's years entirely.");
    }

    [Theory]
    // (S4) obligation 5 — THE TWO RESIDUALS THE REPAIR DELIBERATELY DID NOT CLOSE, pinned rather
    // than merely priced, because a residual nobody pins decays into a change nobody noticed.
    //
    // Both are identical to origin/main and neither is a regression this PR created. Closing them is
    // a change to the LINE grammar (IsIgnorableTail learning a dangling [-/]\d{2}) on a population
    // whose frequency nobody has measured, with its own risk surface ("Acme AB 2000-25") — a
    // genuinely separate change-reason, available as a follow-up PR and not taken here.
    //
    // THE SLASH TWIN OF THIS ROW IS NO LONGER HERE. "2018 – 2019/20" sat beside the hyphen row
    // until ADR 0136, on the reasoning that the two notations shared a residual. They do not any
    // more: the ROW grammar reads a slash point, so that line reduces to empty and both residuals
    // close for it. It moved to TheSlashAcademicYearForm_HasBothResidualsClosed below. The HYPHEN
    // row keeps both residuals, and nothing in ADR 0136 reaches it — ISO 8601 adjudicates the
    // hyphen, so its NN-outside-01-12 case is a different question with a different owner.
    [InlineData("2018 – 2019-20")]
    public void TheAcademicYearForm_IsStillNotADateOnlyLine_WhichLeavesBetaThreeOpenForIt(string line)
    {
        // RESIDUAL 1: the trailing "-20" is not consumed, so the line keeps a non-empty remainder.
        // On the two-line layout that means the date row still becomes the Organization — the β-3
        // fabrication class, still open for this form and only this form.
        DatePatterns.IsDateOnlyLine(line).ShouldBeFalse();

        // RESIDUAL 2: StripDates masks the range but leaves the orphan token, so a prose bullet whose
        // only digits are an academic year can still read as carrying a measurable digit (#487).
        // Asserted through the real consumer, not on the mask string, because the mask exists to
        // answer this question and nothing else reads it.
        ReviewText.ContainsMeasurableDigit($"Ansvarig {line} för budget").ShouldBeTrue(
            "the orphaned academic-year suffix survives masking — unchanged from origin/main.");
    }

    [Theory]
    // THE SLASH ACADEMIC YEAR, moved out of the residual theory above by ADR 0136 — both residuals
    // are closed for it, and each is asserted through its own consumer so neither can be assumed
    // from the other.
    [InlineData("2018 – 2019/20")]
    [InlineData("2020 – 2024/25")]
    public void TheSlashAcademicYearForm_HasBothResidualsClosed(string line)
    {
        // RESIDUAL 1 (β-3): the row grammar reads the slash point, so the line reduces to empty and
        // can no longer become the Organization on the two-line layout.
        DatePatterns.IsDateOnlyLine(line).ShouldBeTrue();

        // RESIDUAL 2 (#487): StripDates reads the same row grammar, so the range is masked whole and
        // no orphan token survives to read as a measurable digit.
        ReviewText.ContainsMeasurableDigit($"Ansvarig {line} för budget").ShouldBeFalse();

        // AND THE VALUE AXIS IS UNMOVED, which is what keeps decision D′'s Blocker closed: the
        // entry still stores the bare-year degradation, and PeriodParser still reads it.
        var period = PeriodFor(line);
        PeriodParser.TryParse(period, out _, out _, out _).ShouldBeTrue(
            $"[{period}] must stay readable — recognising the LINE must never cost the VALUE.");
    }

    [Fact]
    public void TheYearFallbackPath_IsSuppressedRatherThanStoringAConfidentBareYear()
    {
        // (S4) obligation 1, the Year() fallback — and it needs its OWN layout, which is the point.
        // ExtractPeriod tries DateRange over the whole entry text first and only then Year() over
        // Lines[0], so this path is reached when the date row IS the first line. An earlier revision
        // asserted this row's value inside the three-line theory above, having measured it on this
        // shape: a value true of one layout asserted of another, which is the failure this whole
        // lane keeps meeting.
        //
        // THIS TEST ASSERTED THE OPPOSITE UNTIL ADR 0136, calling "2019" "a value both types agree
        // on". Agreement was never the property: the CV states autumn 2019 to 2021 and "2019" parses
        // to a span of start==end, i.e. ZERO years, confidently. ADR 0136's veto suppresses the
        // fallback for a date row the value grammar cannot read, so the entry reports nothing.
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            2019/20 – 2021
            Acme AB
            Ökade konverteringen med 23 procent.
            """;

        var exp = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem();

        exp.Period.ShouldBeNull(
            "a zero-length span claimed for a multi-year tenure is a confident wrong answer, and " +
            "this lane refuses rather than answers (ADR 0071).");
        PeriodParser.TryParse(exp.Period, out _, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void TheYearAxis_IsTheOtherKnownStructuralVsSemanticGap()
    {
        // (S4) obligation 4 — the second named instance of the characterisation in this class's
        // docblock, RUN rather than read (the CTO derived it; this measures it). DateRange takes any
        // \d{4}; PeriodParser enforces 1900-2100. So a quantity or price range in an experience entry
        // is matched and stored, and then correctly refused.
        //
        // Pre-existing, not a regression, and here so the characterisation has more than one instance
        // — a rule with a single example reads as a special case.
        PeriodFor("1500 – 2000").ShouldBe("1500 – 2000");
        PeriodParser.TryParse("1500 – 2000", out _, out _, out _).ShouldBeFalse(
            "the year guard is PeriodParser's, and DateRange does not duplicate it.");
    }

    [Fact]
    public void TheMonthAxis_IsUnchangedByTheStructuralNarrowing()
    {
        // (S4) obligation 3 — the SURVIVING instance, pinned here as well as in its frozen row,
        // because the narrowing deliberately stopped short of it. "13/2020 – 2024" is MM/YYYY, which
        // stands in no prefix relation to any other alternative, so the ordering contract never
        // reached it: this is not a wrong branch beating a right one but a form no branch models.
        // Narrowing it would leave a "13/" residue instead of degrading — flipping IsDateOnlyLine
        // false and handing the date row back to the Organization slot.
        PeriodFor("13/2020 – 2024").ShouldBe("13/2020 – 2024");
        PeriodParser.TryParse("13/2020 – 2024", out _, out _, out _).ShouldBeFalse();
        DatePatterns.IsDateOnlyLine("13/2020 – 2024").ShouldBeTrue(
            "the whole line is still consumed, which is what keeps β-3 closed for this form.");
    }

    [Fact]
    public void TheKeywordLessOpenEnd_StoresNoPeriod_AndThatIsTheHonestAnswer()
    {
        // The one widened form with NO stored period, and it is deliberate rather than an oversight
        // in the theory above. "2020 –" is recognised at the LINE level (IsIgnorableTail) so the
        // organization is not fabricated, but DateRange never matches it — there is no end point —
        // so nothing is extracted. Inventing an end date would be the confidently-wrong half of the
        // defect this lane closes (ADR 0071, honest-absent).
        PeriodFor("2020 –").ShouldBeNull();
        DatePatterns.IsDateOnlyLine("2020 –").ShouldBeTrue(
            "the LINE is still recognised — that is what stops it becoming the employer.");
    }

    [Fact]
    public void AMonthWordEndingThePrecedingLine_IsNotAbsorbedIntoTheStoredPeriod()
    {
        // THE PIN THAT CROSSES THE THRESHOLD OF THE PROPERTY IT PINS. CvMonthNamesTests composes the
        // shared fragment into a test-built regex and checks it rejects a newline — useful, but it
        // cannot fail if someone inlines \s+ back into either production consumer. This one can:
        // ExtractPeriod matches DateRange against entry.Text, which is the entry's lines joined with
        // '\n', so the gap token's line-locality is a property of the SEGMENTER's output.
        //
        // MEASURED before the fix, through this exact path: Period == "maj\n2020 – 2024" — a newline
        // and a word lifted out of a description bullet, riding RawPeriod into the promoted CV on
        // the auto-promote path, which has no approve step.
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB maj
            2020 – 2024
            Ökade konverteringen med 23 procent.
            """;

        var exp = new HeadingDrivenResumeSegmenter(CvParsingLexiconLoader.Load())
            .Segment(cv).Content.Experience.ShouldHaveSingleItem();

        var period = exp.Period.ShouldNotBeNull();
        period.ShouldBe("2020 – 2024",
            "a month point is line-local; the word above the date row belongs to the line above it.");
        period.ShouldNotContain("\n");
    }

    [Fact]
    public void TheDeriver_StillAttributesTheYearsOfAnOngoingMonthNameRole()
    {
        // (S3) obligation 2 — the consumer named in no bind, commit message or session log until the
        // review found it. OccupationExperienceDeriver calls TryParseYearSpan and `continue`s when it
        // fails, so an unparseable Period does not degrade gracefully: the entry contributes ZERO
        // years to its SSYK-4 group, and that is import-time data seeding the matching engine.
        //
        // Asserted on the segmenter's own output, not a hand-built period: the string under test is
        // exactly what ExtractPeriod emits for this CV, which Segment_DateLineTheModelNowReaches
        // pins per form.
        var period = PeriodFor("jan 2020 – nuvarande");

        PeriodParser.TryParseYearSpan(period, currentYear: 2026, out var start, out var end)
            .ShouldBeTrue($"[{period}] must yield a year span, or the role's years are lost entirely.");
        start.ShouldBe(2020);
        end.ShouldBe(2026, "an ongoing role resolves its end to the injected clock year, never DateTime.Now.");
    }

    [Fact]
    public void ALoneMonthPoint_ParsesAsAPeriod_ButIsStillNotADateOnlyLine()
    {
        // (S3) obligation 5 — the blast radius of widening PointRegex, pinned in the three places it
        // reaches, INCLUDING the one where nothing may change.
        //
        // (a) PeriodParser accepts the lone point. It already did for "2020" and "03/2020"; A
        //     extends that, it does not invent it.
        PeriodParser.TryParse("maj 2020", out var date, out _, out var token).ShouldBeTrue();
        date.ShouldBe(new DateOnly(2020, 5, 1));
        token.ShouldBe("MM/YYYY");

        // (b) DatePatterns still declines it, and THIS is the important half. #428 settled that a
        //     lone date on a non-header line must not be read as a period — the range separator is
        //     what tells a period from a date mentioned in prose. The two types disagree here on
        //     purpose, and that documented disagreement must survive the widening intact.
        DatePatterns.IsDateOnlyLine("maj 2020").ShouldBeFalse();
        DatePatterns.StripTrailingDate("maj 2020").ShouldBe("maj",
            "Year() takes the 2020 and the month word is left behind — unchanged by road 3.");
    }

    [Fact]
    public void ALoneMonthPointLine_IsNowSuppressedFromTheBullets_WhichIsTheDirectionThatIsAllowed()
    {
        // (S3) obligation 5(a) — the union at ReviewText.DescriptionLines is
        // `PeriodParser.TryParse(line, …) || DatePatterns.IsDateOnlyLine(line)`, so widening the LEFT
        // disjunct widens suppression. That is the permitted direction: suppression may grow, never
        // narrow. A line that is nothing but "maj 2020" is not a description bullet.
        const string bullet = "Ökade konverteringen med 23 procent.";
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            2013 - 2021
            maj 2020
            {bullet}
            """;

        var parsed = ResumeFromCvText(cv);
        var reviewable = CvReviewContext.FromParsed(parsed).Content.Experience.ShouldHaveSingleItem();

        ReviewText.DescriptionLines(reviewable).ShouldBe([bullet],
            "a lone month point is a date, not prose — suppression grew, which is the allowed direction.");
    }
}
