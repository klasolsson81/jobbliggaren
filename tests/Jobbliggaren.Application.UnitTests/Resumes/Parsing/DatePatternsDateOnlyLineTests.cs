using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1060 β-3 follow-up — the promoted predicate's OWN surface.
/// <c>DatePatterns.StripTrailingDate</c> / <c>DatePatterns.IsDateOnlyLine</c> became public
/// assembly-internal API (<c>DatePatterns</c> is <c>internal</c>, so the consumer set is closed
/// and named) with THREE call sites across two types (<c>HeadingDrivenResumeSegmenter.StripTrailingPeriod</c>
/// for the reduction, <c>HeadingDrivenResumeSegmenter.SplitTitleOrganization</c> for the predicate
/// and <c>ReviewText.DescriptionLines</c>), and until this file it had no direct test of its
/// own: both readers exercised it only through their own behaviour, so the shared contract was
/// asserted nowhere. A predicate two engines depend on is pinned where it lives.
///
/// <para><b>What the contract IS.</b> <c>StripTrailingDate</c> removes a date match that runs to
/// the END of the line, plus the separators left dangling behind it; a leading or internal year
/// is left alone ("Studio 2005 Design"). <c>IsDateOnlyLine</c> is that reduction asked as a
/// question — true exactly when the reduction empties the line. <b>The two are asserted TOGETHER on
/// every input this class carries</b>, because the definitional identity
/// (<c>IsDateOnlyLine(x) == (StripTrailingDate(x).Length == 0)</c>) is the whole substance of the
/// promotion: it is what makes one home one home rather than two copies. Most cases reach it through
/// <c>ShouldReduceTo</c>; the two that cannot — <c>ForALoneSlashPoint</c>, where the reduction is not
/// a no-op, and <c>PeriodParser_ShouldNotReach_HyphenAsAMonthSeparator</c>, which pins the OTHER
/// grammar — spell both sides out inline. <b>A row asserting <c>IsDateOnlyLine</c> alone would be a
/// hole in exactly the drift check this file exists to be</b>; one was introduced mid-review and both
/// reviewers caught it independently.</para>
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
    // Most cases in this class run through this helper rather than asserting IsDateOnlyLine alone.
    // A future edit that re-implements IsDateOnlyLine as a second copy — the precise thing the
    // promotion exists to prevent — can drift from StripTrailingDate only by breaking this
    // identity, so the identity is asserted on every input the class carries, positive and
    // negative alike. The two cases that do not use the helper assert BOTH sides inline instead,
    // because their reduction is not the helper's `expected == line` or `expected == ""` shape;
    // neither is an exemption from the identity.
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
        PeriodParser.TryParse(line, out _, out _, out _).ShouldBeFalse(
            $"PeriodParser declines this form too, so the union does not rescue it either. The " +
            $"DatePatterns-side reason is: {why}.");
    }

    [Theory]
    // THE AXES ON WHICH PeriodParser IS WIDER — the measurement that makes ReviewText's period
    // test a UNION and not a substitution.
    //
    // THIS LIST IS THE ADJUDICATOR, AND IT IS NOT A COUNT. The CTO bind said "at least three
    // axes"; a later revision hardened that to "four" and code-reviewer then measured a fifth.
    // The hedge was the correct part and removing it was the defect: the number is emergent from
    // two independently written grammars, so any total decays the moment either changes — and the
    // date-model widening changes one of them. Add rows here; publish no total anywhere.
    [InlineData("2019 till 2021", "the word separator 'till'")]
    [InlineData("2019 to 2021", "the word separator 'to'")]
    [InlineData("3/2020 – 6/2024", "single-digit months (\\d{1,2} against DatePatterns' \\d{2})")]
    [InlineData("03.2020 – 06.2024",
        "'.' as the month separator, where DatePatterns' \\d{2}/\\d{4} takes only '/'")]
    [InlineData("2020-06 – 2024-03",
        "an ISO YYYY-MM END point: DateRange's end-alternation takes the bare \\d{4} first and " +
        "leaves '-03' as a non-empty tail, so the whole line is never consumed")]
    [InlineData("2020-06",
        "a lone ISO YYYY-MM point: DateRange needs a separator and an end point, and Year leaves " +
        "'-06' as a non-empty tail")]
    public void IsDateOnlyLine_ShouldBeFalse_WhereOnlyPeriodParserReachesTheForm(
        string line, string axis)
    {
        // This is the direction of the trap. Swapping ReviewText's PeriodParser test for a
        // DatePatterns-only one would hand exactly these lines to the review side's bullet scorer
        // (ReviewText.ExperienceBullets → A1/A2/A6), where a criterion can cite the user's
        // employment dates as if they were prose — §5's "a CV verdict without cited textual
        // evidence" in its inverted form. NOT WeakVerbTransform: it proposes only for a bullet
        // opening with a drop-in-safe weak verb, so it is offered a date row and declines it.
        ShouldReduceTo(line, line);
        PeriodParser.TryParse(line, out _, out _, out _).ShouldBeTrue(
            $"PeriodParser reaches this form and DatePatterns does not — {axis}.");
    }

    /// <summary>
    /// The same PeriodParser-is-wider axis as above — a lone date POINT with no range separator —
    /// but with a REDUCTION that is not a no-op, which is why it cannot share that theory.
    ///
    /// <para>Written after the first attempt asserted a no-op here and the run refused it. The
    /// analysis behind that attempt was right about the PREDICATE (<c>"03/"</c> is non-empty, so
    /// <c>IsDateOnlyLine</c> is false) and wrong about the REDUCTION: <c>Year</c> matches "2020" at
    /// index 3 with an empty tail, so the line IS reduced — to <c>"03/"</c>, because "/" is not in
    /// the trailing-separator set. Two forms of the same axis, two different reductions, and the
    /// helper's ShouldBe caught the conflation.</para>
    ///
    /// <para>Half of this axis was already pinned in the tree — <c>PeriodParserYearSpanTests</c>
    /// carries "single MM/YYYY point" — and nobody had read it against this predicate.</para>
    /// </summary>
    [Fact]
    public void IsDateOnlyLine_ShouldBeFalse_ForALoneSlashPoint_EvenThoughTheLineIsReduced()
    {
        DatePatterns.StripTrailingDate("03/2020").ShouldBe("03/",
            "Year matches the bare year with an empty tail, so the line reduces — but '/' is not a " +
            "trailing separator, so the remainder is non-empty.");
        DatePatterns.IsDateOnlyLine("03/2020").ShouldBeFalse(
            "a non-empty remainder means the line carries more than a date, as far as this model " +
            "is concerned.");
        PeriodParser.TryParse("03/2020", out _, out _, out _).ShouldBeTrue(
            "PeriodParser parses a lone MM/YYYY point, so only its disjunct suppresses this row.");
    }

    /// <summary>
    /// A NEGATIVE pin on the axis list itself: "-" as a MONTH separator is NOT a
    /// PeriodParser-is-wider axis, even though <c>PeriodParser.PointRegex</c> lists <c>[/.\-]</c>.
    ///
    /// <para>Found by dotnet-architect against a head whose docblock claimed <c>"." / "-"</c> as one
    /// axis. It is the only prose axis that had no adjudicating <c>InlineData</c> — written into the
    /// very paragraph that had just named this file the adjudicator. So it is pinned rather than
    /// merely corrected: a claim about <c>PeriodParser</c>'s grammar that no run adjudicates is the
    /// same class of defect as the axis COUNT that preceded it.</para>
    ///
    /// <para>The mechanism: <c>SeparatorRegex</c> splits BEFORE <c>PointRegex</c> ever runs, and its
    /// <c>(?&lt;!\d{4})-</c> alternative sees only two digits before the hyphen of "03-2020", so the
    /// lookbehind succeeds and the hyphen is consumed as a RANGE split. That leaves "03" as the left
    /// point, which matches neither branch. The hyphen survives as a point-internal separator only
    /// when exactly four digits precede it — the ISO form, which is year-first, not MM-first.
    /// <b>Road 3 touches this grammar; this pin is what will catch it.</b></para>
    /// </summary>
    [Theory]
    [InlineData("03-2020")]
    [InlineData("03-2020 – 06-2024")]
    public void PeriodParser_ShouldNotReach_HyphenAsAMonthSeparator(string line)
    {
        PeriodParser.TryParse(line, out _, out _, out _).ShouldBeFalse(
            "SeparatorRegex consumes the hyphen as a range split before PointRegex sees it, so " +
            "PointRegex's [/.\\-] hyphen branch is unreachable for an MM-first point.");
        DatePatterns.IsDateOnlyLine(line).ShouldBeFalse(
            "and DatePatterns does not model it either — so this form is suppressed by NEITHER " +
            "disjunct, which is what makes it wrong to list as an axis on which PeriodParser wins.");
    }

    [Theory]
    // THE OTHER DIRECTION, and it is why the union is a union rather than "PeriodParser plus one
    // extra case". The head that said "four axes" also said this predicate is wider "only for a
    // leading separator"; that was the same over-claim mirrored, and code-reviewer measured the
    // second form below. Both rows here are suppressed ONLY by the DatePatterns disjunct.
    [InlineData("– 2020 – 2024",
        "a LEADING separator, which PeriodParser's ^…$ anchoring refuses")]
    [InlineData("13/2020 – 2024",
        "a structurally-matching range whose MONTH is out of range: DateRange does not validate " +
        "the month, PeriodParser does and declines")]
    public void IsDateOnlyLine_ShouldBeTrue_WhereOnlyDatePatternsReachesTheForm(
        string line, string axis)
    {
        // Through the helper, not IsDateOnlyLine alone. The first revision of this theory asserted
        // the predicate on its own and thereby falsified this file's own universality claim — the
        // identity is stated as holding on EVERY input the class carries, and "13/2020 – 2024"
        // became the one input carrying no identity assertion at all. Both reviewers found it
        // independently. A file whose purpose is to make drift impossible must not carry a row the
        // drift check skips.
        ShouldReduceTo(line, string.Empty);
        PeriodParser.TryParse(line, out _, out _, out _).ShouldBeFalse(
            $"PeriodParser declines this form — {axis}.");
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
        // pinned because the method is shared Infrastructure API that must not throw on the
        // degenerate input, NOT as a claim about what production does with an empty line.
        ShouldReduceTo(string.Empty, string.Empty);
    }
}
