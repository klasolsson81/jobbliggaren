using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1060 β-3 follow-up — the promoted predicate's OWN surface.
/// <c>DatePatterns.StripTrailingDate</c> / <c>DatePatterns.IsDateOnlyLine</c> became public
/// Infrastructure API with TWO readers (<c>HeadingDrivenResumeSegmenter.StripTrailingPeriod</c>
/// and <c>ReviewText.DescriptionLines</c>), and until this file it had no direct test of its
/// own: both readers exercised it only through their own behaviour, so the shared contract was
/// asserted nowhere. A predicate two engines depend on is pinned where it lives.
///
/// <para><b>What the contract IS.</b> <c>StripTrailingDate</c> removes a date match that runs to
/// the END of the line, plus the separators left dangling behind it; a leading or internal year
/// is left alone ("Studio 2005 Design"). <c>IsDateOnlyLine</c> is that reduction asked as a
/// question — true exactly when the reduction empties the line. The two are asserted TOGETHER in
/// every case below, because the definitional identity
/// (<c>IsDateOnlyLine(x) == (StripTrailingDate(x).Length == 0)</c>) is the whole substance of the
/// promotion: it is what makes one home one home rather than two copies.</para>
///
/// <para><b>The four unmodelled forms are on the NEGATIVE side, deliberately.</b>
/// "jan 2020 – dec 2024", "2020 – 2024 (heltid)", "2020/01 – 2024/12" and "2020 –" are the
/// segmenter pin's frozen negative population
/// (<c>Segment_DateLineDatePatternsDoesNotModel_IsStillTakenAsTheOrganization</c>). They are
/// pinned here too — at the predicate rather than at one reader — so the gap has a measurement in
/// the type that owns it. <b>The trigger that reddens both is a DatePatterns WIDENING</b>
/// (month names, trailing qualifiers, keyword-less open ends, YYYY/MM), which is the deferred
/// follow-up PR. If one of them starts passing, the widening landed; it is not a stale fixture.</para>
/// </summary>
public class DatePatternsDateOnlyLineTests
{
    // ── The predicate is exactly the reduction, asserted as an identity ──────────────
    //
    // Every case in this class runs through this helper rather than asserting IsDateOnlyLine
    // alone. A future edit that re-implements IsDateOnlyLine as a second copy — the precise
    // thing the promotion exists to prevent — can drift from StripTrailingDate only by breaking
    // this identity, so the identity is asserted on every input the class carries, positive and
    // negative alike.
    private static void ShouldReduceTo(string line, string expected)
    {
        DatePatterns.StripTrailingDate(line).ShouldBe(expected,
            $"StripTrailingDate should reduce [{line}] to [{expected}].");
        DatePatterns.IsDateOnlyLine(line).ShouldBe(expected.Length == 0,
            "IsDateOnlyLine IS StripTrailingDate asked as a question — one home, not two copies.");
    }

    // ── POSITIVE: the line carries a date and nothing else ───────────────────────────

    [Theory]
    // The two range notations the segmenter's own fixtures use, spaced and unspaced.
    [InlineData("2005 - 2010")]
    [InlineData("2005-2010")]
    [InlineData("2013–2021")]
    // A bare year is a whole period on its own (#428).
    [InlineData("2005")]
    // MM/YYYY start points — the \d{2}/\d{4} form DateRange models.
    [InlineData("01/2022 – nuvarande")]
    [InlineData("12/2019 - 03/2024")]
    // Open ends WITH a present-keyword, Swedish and abbreviated.
    [InlineData("2024 - nu")]
    [InlineData("2020 - pågående")]
    // Surrounding whitespace: the end-of-line test trims the tail, so an indented period line
    // (a two-column DOCX renders these) still reduces to empty.
    [InlineData("  2005 - 2010  ")]
    public void IsDateOnlyLine_ShouldBeTrue_WhenTheWholeLineIsADate(string line) =>
        ShouldReduceTo(line, string.Empty);

    [Fact]
    public void IsDateOnlyLine_ShouldBeTrue_WhenTheDateCarriesALeadingSeparator()
    {
        // THE β-3 RESIDUAL THIS PROMOTION CLOSED, and the one axis on which this predicate is
        // WIDER than PeriodParser. PeriodParser anchors ^…$ and splits on the FIRST separator,
        // so a leading "– " gives it an empty left point and it refuses the line outright.
        // StripTrailingDate matches the range unanchored and then trims the "– " left behind,
        // which is why ReviewText's period test had to gain this predicate rather than swap to it.
        ShouldReduceTo("– 2020 – 2024", string.Empty);
        PeriodParser.TryParse("– 2020 – 2024", out _, out _, out _).ShouldBeFalse(
            "the union's DatePatterns half is load-bearing precisely because PeriodParser refuses this.");
    }

    // ── NEGATIVE: the line carries something besides a date ──────────────────────────

    [Theory]
    // A year INSIDE a name is not a period — the reduction is end-of-line only, by design
    // (the segmenter comment names "Studio 2005 Design" as the reason).
    [InlineData("Studio 2005 Design")]
    // Ordinary prose, with and without digits: a bullet must never look like a date row.
    [InlineData("Systemutvecklare")]
    [InlineData("Ökade konverteringen med 23 procent")]
    // Punctuation AFTER the match: the trim runs on the remainder to the LEFT of the match, never
    // on the tail, so anything trailing the date keeps the line non-empty.
    [InlineData("2005 - 2010,")]
    [InlineData("2005 - 2010;")]
    [InlineData("2005 – 2010 | Acme AB")]
    // Whitespace-only. NOT the same input as the empty string below and NOT the same answer:
    // neither pattern matches, so the line is returned verbatim and stays non-empty.
    [InlineData("   ")]
    public void IsDateOnlyLine_ShouldBeFalse_WhenTheLineCarriesMoreThanADate(string line) =>
        ShouldReduceTo(line, line);

    [Theory]
    [InlineData("jan 2020 – dec 2024", "no month token in the end-alternation")]
    [InlineData("2020 – 2024 (heltid)", "a qualifier follows the match")]
    [InlineData("2020/01 – 2024/12", "YYYY/MM is not a modelled point form")]
    [InlineData("2020 –", "DateRange needs an end point; Year leaves a non-empty tail")]
    public void IsDateOnlyLine_ShouldBeFalse_WhenTheDateFormIsOneDatePatternsDoesNotModel(
        string line, string why)
    {
        // ACCEPTED-AND-KNOWN, pinned at the predicate that owns the gap. These are the same four
        // forms the segmenter pin freezes, and they are here for a reason that pin cannot serve:
        // that test measures a CONSEQUENCE (the line becomes the organization), which a change
        // elsewhere in the segmenter could mask. This measures the CAUSE.
        ShouldReduceTo(line, line);

        // And PeriodParser declines all four too — so the ReviewText union does not rescue them
        // either. Naming it here keeps the promotion's blast radius honest: it factored today's
        // model into one home and inherited its blind spot, exactly as predicted.
        PeriodParser.TryParse(line, out _, out _, out _).ShouldBeFalse(why);
    }

    [Theory]
    // THE AXES ON WHICH PeriodParser IS WIDER — the measurement that makes ReviewText's period
    // test a UNION and not a substitution. Three are named in the CTO bind (word separators,
    // \d{1,2} months, "."/"-" month separators); the FOURTH was found by measuring rather than
    // by reading the table, see the ISO case below.
    [InlineData("2019 till 2021", "the word separator 'till'")]
    [InlineData("3/2020 – 6/2024", "single-digit months (\\d{1,2} against DatePatterns' \\d{2})")]
    [InlineData("2020-06 – 2024-03",
        "an ISO YYYY-MM END point: DateRange's end-alternation takes the bare \\d{4} first and " +
        "leaves '-03' as a non-empty tail, so the whole line is never consumed")]
    public void IsDateOnlyLine_ShouldBeFalse_WhereOnlyPeriodParserReachesTheForm(
        string line, string axis)
    {
        // This is the direction of the trap. Swapping ReviewText's PeriodParser test for a
        // DatePatterns-only one would hand exactly these lines to the bullet scorer and to
        // WeakVerbTransform, which proposes a rewrite of every bullet — a §5 CV-engine class.
        ShouldReduceTo(line, line);
        PeriodParser.TryParse(line, out _, out _, out _).ShouldBeTrue(
            $"PeriodParser reaches this form and DatePatterns does not — {axis}.");
    }

    [Theory]
    // The reduction's real job in the segmenter: a header line that packs the dates onto the
    // role/company line must yield the FIELDS, with the dangling separator gone.
    [InlineData("Acme AB 2005 - 2010", "Acme AB")]
    [InlineData("Acme AB, 2005", "Acme AB")]
    [InlineData("Plasman — Operatör 2005 – nu", "Plasman — Operatör")]
    [InlineData("Konsult | Egen firma 2018 – 2022", "Konsult | Egen firma")]
    public void StripTrailingDate_ShouldReturnTheFieldsAndDropTheDanglingSeparator(
        string line, string expected) =>
        ShouldReduceTo(line, expected);

    [Fact]
    public void IsDateOnlyLine_ShouldBeTrue_WhenTheLineIsEmpty()
    {
        // TOTALITY, and DECLARED UNREACHABLE from either reader (CLAUDE.md §5). Neither call site
        // can produce it: HeadingDrivenResumeSegmenter's entries carry only non-blank lines, and
        // ReviewText.DescriptionLines filters `l.Length > 0` before the period test runs. It is
        // pinned because the method is public Infrastructure API that must not throw on the
        // degenerate input, NOT as a claim about what production does with an empty line.
        ShouldReduceTo(string.Empty, string.Empty);
    }
}
