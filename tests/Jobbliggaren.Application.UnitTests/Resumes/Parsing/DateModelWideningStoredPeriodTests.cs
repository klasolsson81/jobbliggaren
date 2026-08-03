using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1060 road 3 — THE INVARIANT <c>DatePatterns</c>' docblock has always claimed and nothing ever
/// tested: <b>whatever <c>ExtractPeriod</c> stores, <c>PeriodParser</c> must be able to read</b>
/// ((S3) obligations 1, 2 and 5; senior-cto-advisor re-bind 2026-08-03).
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
    [InlineData("2020 – 2024/12", "YYYY")]
    [InlineData("2020-06 – 2024/12", "MM/YYYY")]
    // THE SYMMETRIC CLASS — both endpoints widened. These had NO period at all before; they are here
    // because the invariant is universal over what the segmenter stores, not over what regressed.
    [InlineData("jan 2020 – dec 2024", "MM/YYYY")]
    [InlineData("2020/01 – 2024/12", "MM/YYYY")]
    // Controls the widening must not disturb.
    [InlineData("2013 - 2021", "YYYY")]
    [InlineData("2020-06 – 2024-03", "MM/YYYY")]
    [InlineData("2020 – 2024 (heltid)", "YYYY")]
    public void WhateverTheSegmenterStores_ThePeriodParserCanRead(string dateLine, string expectedToken)
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
