using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #1060 β-3 follow-up — the promoted predicate's OWN surface.
/// <c>DatePatterns.StripTrailingDate</c> / <c>DatePatterns.IsDateOnlyLine</c> became
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
/// promotion: it is what makes one home one home rather than two copies. <b>Every case reaches it
/// through <c>ShouldReduceTo</c></b> — the helper takes an arbitrary expected reduction, so a case
/// whose line is neither left whole nor emptied uses it too. <b>A row asserting
/// <c>IsDateOnlyLine</c> alone is a hole in exactly the drift check this file exists to be</b>; two
/// were introduced mid-review, on consecutive rounds, and the reviewers caught both.</para>
///
/// <para><b>Four unmodelled forms were on the NEGATIVE side, and the WIDENING moved THREE of them
/// (#1060 road 3).</b> The paragraph that stood here said: <i>"'jan 2020 – dec 2024',
/// '2020 – 2024 (heltid)', '2020/01 – 2024/12' and '2020 –' are the segmenter pin's frozen negative
/// population … The trigger that reddens both is a DatePatterns WIDENING … If one of them starts
/// passing, the widening landed; it is not a stale fixture."</i> Three landed and pass, and the
/// <c>InlineData</c> for them were carried across unchanged with only their assertions moved — the
/// replacement that paragraph asked for, not an edit to keep a fixture green. <b>The fourth,
/// <c>YYYY/MM</c>, passed for exactly one commit and was taken back OUT</b> (round 5): it collided
/// with the Swedish läsår notation and a mixed-endpoint form of it stored a value neither engine
/// could read. It lives in <c>DateRangeYearFirstCharacterisationTests</c>, which owns the
/// year-first grammar.</para>
///
/// <para><b>The surviving three sit in TWO theories, and the split is the design made visible.</b>
/// The month-name POINT form is matched by <c>DateRange</c>, whose match value
/// <c>ExtractPeriod</c> stores — so it is also in the point grammar <c>PeriodParser</c> shares, and
/// BOTH predicates reach it. The two LINE forms are answered by <c>IsIgnorableTail</c> and
/// deliberately never enter the match value, so <c>PeriodParser</c> still declines them and each
/// stays an independent kill for "union → PeriodParser only". This file also pins the MM-hyphen-point
/// axis in lone and left position, the qualifier's restriction to the range branch, and the lone
/// month-point residual the widening did not close; no total is claimed for the class itself.</para>
/// </summary>
public class DatePatternsDateOnlyLineTests
{
    // ── The predicate is exactly the reduction, asserted as an identity ──────────────
    //
    // EVERY case in this class runs through this helper rather than asserting IsDateOnlyLine
    // alone. A future edit that re-implements IsDateOnlyLine as a second copy — the precise thing
    // the promotion exists to prevent — can drift from StripTrailingDate only by breaking this
    // identity, so the identity is asserted on every input the class carries, positive and
    // negative alike.
    //
    // `expected` is arbitrary, and that is load-bearing: an earlier round claimed the helper only
    // took `line` or `""` and used that as grounds to assert IsDateOnlyLine inline instead. It was
    // false — StripTrailingDate_ShouldReturnTheFieldsAndDropTheDanglingSeparator passes "Acme AB"
    // through it — and the false premise is what produced the hole. There is no exemption.
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
    [InlineData("jan 2020 – dec 2024", "a month-NAME point, Swedish or English, in both endpoints")]
    public void IsDateOnlyLine_ShouldBeTrue_ForThePointFormsTheWideningAdded(string line, string how)
    {
        // THE POINT HALF OF THE WIDENING (#1060 road 3). These two are new POINT forms, so they live
        // in DateRange's alternation — and therefore in the grammar PeriodParser shares, since
        // DateRange's match VALUE is what ExtractPeriod stores and PeriodParser is what consumes it.
        //
        // BOTH PREDICATES REACH THEM, and that is the design rather than a redundancy: the union at
        // ReviewText suppresses the row either way, while the shared point grammar is what keeps the
        // stored Period readable. An earlier revision of this theory asserted PeriodParser DECLINED
        // them — true for exactly as long as the two grammars were out of step, which was the
        // regression (DateModelWideningStoredPeriodTests owns that measurement).
        ShouldReduceTo(line, string.Empty);
        PeriodParser.TryParse(line, out _, out _, out _).ShouldBeTrue(
            $"a POINT form the segmenter can extract must be one PeriodParser can read — {how}.");
    }

    [Theory]
    [InlineData("2020 – 2024 (heltid)", "a trailing parenthesised qualifier, tolerated in the TAIL")]
    [InlineData("2020 –", "a keyword-less open end: a dangling range separator in the TAIL")]
    // YYYY/MM ("2020/01 – 2024/12") passed through this theory for one commit (Klas-direktiv
    // 2026-08-03) and was taken back OUT of it in round 5: the year-first SLASH notation is a YEAR
    // PAIR in Swedish — a läsår — and DateRange models it on NEITHER endpoint. That form lives in
    // DateRangeYearFirstCharacterisationTests with the rest of the year-first grammar, not here —
    // the same cross-reference pattern "13/2020 – 2024" uses below, for the same reason: that table
    // is indexed by the grammar's own axes.
    public void IsDateOnlyLine_ShouldBeTrue_ForTheLineFormsTheWideningAdded(string line, string how)
    {
        // THE LINE HALF, and the split from the theory above IS the design decision, made visible.
        // These two are NOT point forms: they are properties of the whole line, answered by
        // IsIgnorableTail, and they deliberately never enter DateRange's match value. Putting the
        // qualifier in the match would store Period = "2020 – 2024 (heltid)", which PeriodParser
        // refuses — turning a period that parses today into one that does not.
        ShouldReduceTo(line, string.Empty);

        // SO PeriodParser STILL DECLINES THESE TWO, and each is an independent kill for
        // "union → PeriodParser only": drop the DatePatterns disjunct and the date row reaches the
        // bullet scorer again. That "2020 – 2024 (heltid)" still yields a PARSEABLE stored period
        // ("2020 – 2024", the match value without the tail) is the point of the split and is pinned
        // in DateModelWideningStoredPeriodTests.
        PeriodParser.TryParse(line, out _, out _, out _).ShouldBeFalse(
            $"PeriodParser declines this whole-line form, so only the DatePatterns half suppresses " +
            $"it. The DatePatterns-side mechanism is: {how}.");
    }

    [Theory]
    // THE PARENTHESISED QUALIFIER IS ACCEPTED AFTER A RANGE ONLY, NEVER AFTER A BARE YEAR
    // (senior-cto-advisor re-bind 2026-08-03 §5). A bare year is the weaker date signal — this
    // file already pins "Studio 2005 Design" negative on exactly that ground — so licensing a
    // bracket strip after one would treat a line one glyph away from a pinned negative as a date.
    // "(publ)" is the standard suffix of a Swedish public limited company, and truncating
    // "Acme AB 2000 (publ)" to "Acme AB" silently drops part of a real organization value.
    //
    // The RANGE form with the same qualifier IS accepted, two theories above — that asymmetry is
    // the ruling, and these rows are what make it a behaviour rather than an intention.
    [InlineData("Acme AB 2000 (publ)")]
    [InlineData("Studio 2005 (Design)")]
    [InlineData("Konsult 2019 (via Bolaget AB)")]
    public void IsDateOnlyLine_ShouldBeFalse_ForAQualifierAfterABareYear(string line) =>
        ShouldReduceTo(line, line);

    [Theory]
    // THE RESIDUAL THE WIDENING DID NOT CLOSE, named rather than left implicit. A month name is
    // recognised only INSIDE a range: a LONE month point is not reduced, exactly as a lone "03/2020"
    // is not (that row lives in the PeriodParser-is-wider theory above). Year() takes the "2020" and
    // leaves the month word behind, so the line keeps a non-empty remainder.
    //
    // This is a scope statement, not a defect claim: #428 already settled that a lone bare year on a
    // non-header line must NOT be read as a period, and the range separator is what tells a period
    // apart from a date mentioned in prose. Widening the lone-point case has a different blast
    // radius — it would change what ExtractPeriod recovers, not only what a line is judged to be —
    // and it is a separate change-reason.
    [InlineData("maj 2020", "maj")]
    [InlineData("januari 2020", "januari")]
    public void IsDateOnlyLine_ShouldBeFalse_ForALoneMonthPoint_WhichIsNotARange(
        string line, string reduced) =>
        ShouldReduceTo(line, reduced);

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
    // '-' as the month separator, but ONLY in the RIGHT point. SeparatorRegex splits on the FIRST
    // separator and Split(trimmed, 2) never splits the right part again, so the en dash is
    // consumed as the range split and "03-2024" reaches PointRegex's [/.\-] branch whole. Measured,
    // not derived: an earlier revision of this file asserted the OPPOSITE as a universal, and the
    // run refuted it. See the lone/left-point pin below for the position where it does NOT hold.
    [InlineData("2020 – 03-2024",
        "'-' as the month separator in the RIGHT point of a range")]
    [InlineData("01/2022 - 06-2024",
        "'-' in the right point again, after a hyphen-with-spaces range separator")]
    // The ISO YYYY-MM END point ("2020-06 – 2024-03") used to be a row here — the axis where
    // DateRange's end-alternation took the bare \d{4} first and left "-03" as a non-empty tail.
    // #1060 road 3 commit 1 ordered both alternations longest-alternative-first, so DatePatterns
    // now reaches that form and the axis is retired. The row MOVED to
    // DatePatternsAlternationOrderingTests, which pins the correction on all three surfaces; it was
    // not edited in place, because its subject changed sides rather than its expectation drifting.
    [InlineData("2020-06",
        "a lone ISO YYYY-MM point: DateRange needs a separator and an end point, and Year leaves " +
        "'-06' as a non-empty tail")]
    public void IsDateOnlyLine_ShouldBeFalse_WhereOnlyPeriodParserReachesTheForm(
        string line, string axis)
    {
        // This is the direction of the trap. B5 does NOT act on these rows — every line here
        // opens with a digit, so StructureRules' LeadMarker returns null — which is why only
        // A1/A2/A6 are named below. Nor does it act on the leading-separator direction: LeadMarker
        // nulls any marker whose remainder PeriodParser parses, which that direction always
        // satisfies by construction. B5 reads DescriptionLines and CAN act — but only where the
        // remainder is a form PeriodParser refuses ("– jan 2020 – dec 2024"), which is the live
        // escape, not either form this union releases.
        //
        // Swapping ReviewText's PeriodParser test for a
        // DatePatterns-only one would release exactly these lines into the review side's bullet
        // scorer (ReviewText.ExperienceBullets → A1/A2/A6) AND into WeakVerbTransform's bullet
        // unit, which IS DescriptionLines. The transform is offered the row and DECLINES to
        // propose on it — it fires only for a bullet opening with a drop-in-safe weak verb from
        // the KnowledgeBank mapping — so only the review side acts.
        //
        // What the review side then does — cite the user's employment dates as though they were
        // prose, §5's "a CV verdict without cited textual evidence" inverted — was DERIVED when this
        // was written and is now MEASURED (#1060 road 3, (S1)), and the widening closed it for the
        // forms it reaches. DateModelWideningReviewSideTests is the adjudicator; do not restate this
        // as derived.
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
        // Through the helper like everything else, and the arbitrary `expected` is what makes that
        // possible: Year matches "2020" with an empty tail, so the line IS reduced — to "03/",
        // because "/" is not in the trailing-separator set, leaving a non-empty remainder and a
        // false predicate. An earlier round asserted both sides inline here on the false grounds
        // that the helper only took `line` or `""`.
        ShouldReduceTo("03/2020", "03/");
        PeriodParser.TryParse("03/2020", out _, out _, out _).ShouldBeTrue(
            "PeriodParser parses a lone MM/YYYY point, so only its disjunct suppresses this row.");
    }

    /// <summary>
    /// The POSITION where "-" as a month separator stops being a PeriodParser-is-wider axis: a lone
    /// point, or the LEFT point of a range. There the hyphen is consumed as the range split itself.
    ///
    /// <para><b>This pin exists because a universal claim was published in its place and refuted by
    /// measurement.</b> A revision of this file asserted that <c>PointRegex</c>'s <c>[/.\-]</c>
    /// branch is unreachable for any MM-first point, and put that in <c>DatePatterns</c>' docblock
    /// too. Both reviewers found the counter-example independently, and a run settled it:
    /// <c>"2020 – 03-2024"</c> and <c>"01/2022 - 06-2024"</c> both parse, because
    /// <c>SeparatorRegex</c> splits on the FIRST separator and <c>Split(trimmed, 2)</c> never splits
    /// the right part again. Those rows now sit in the wider-axis theory above; what survives here
    /// is the narrower, true statement. <b>Swapping a wrong COUNT for a wrong UNIVERSAL is the same
    /// defect class, and a false claim carrying an adjudicator reads as measured.</b></para>
    ///
    /// <para>Both rows go through <c>ShouldReduceTo</c> with reductions taken from the run, not
    /// derived: <c>"03-2020"</c> reduces to <c>"03"</c> (<c>Year</c> matches "2020" with an empty
    /// tail and the "-" is trimmed), while <c>"03-2020 – 06-2024"</c> is not reduced at all.
    /// <b>Road 3 touches this grammar; this pin is what will catch it.</b></para>
    /// </summary>
    [Theory]
    [InlineData("03-2020", "03")]
    [InlineData("03-2020 – 06-2024", "03-2020 – 06-2024")]
    public void IsDateOnlyLine_ShouldBeFalse_ForAnMmHyphenPointBeforeAnyOtherSeparator(
        string line, string reduced)
    {
        ShouldReduceTo(line, reduced);
        PeriodParser.TryParse(line, out _, out _, out _).ShouldBeFalse(
            "with the hyphen leading, SeparatorRegex consumes it as the range split before " +
            "PointRegex sees it, leaving a bare month that matches no branch. This is a claim " +
            "about POSITION only — in a right point the same separator parses (see the theory above).");
    }

    [Theory]
    // THE OTHER DIRECTION, and it is why the union is a union rather than "PeriodParser plus one
    // extra case". The head that said "four axes" also said this predicate is wider "only for a
    // leading separator"; that was the same over-claim mirrored, and code-reviewer measured the
    // second form below. Both rows here are suppressed ONLY by the DatePatterns disjunct.
    [InlineData("– 2020 – 2024",
        "a LEADING separator, which PeriodParser's ^…$ anchoring refuses")]
    // The reason narrowed twice. DateRange validates the month structurally in its year-first
    // branches — but only in the END alternation; the START alternation deliberately keeps the
    // loose form, because order only bites where a short branch can succeed. This row is MM/YYYY,
    // which stands in no prefix relation to any other alternative, so the ordering contract never
    // reached it at all and narrowing it would leave a "13/" residue instead of degrading.
    //
    // NOT the only instance of this axis: "2019-20 – 2021" (an academic year in START position)
    // is reached by DatePatterns and refused by PeriodParser for the same structural-vs-semantic
    // reason. It lives in DateRangeYearFirstCharacterisationTests with the rest of the year-first
    // grammar rather than here, because that table is indexed by the grammar's axes — but this
    // list says of itself "THIS LIST IS THE ADJUDICATOR … Add rows here", so the cross-reference
    // is the row's stand-in and is deliberate rather than an omission.
    [InlineData("13/2020 – 2024",
        "a structurally-matching range whose MONTH is out of range: DateRange validates the month " +
        "only in its year-first branches, not in MM/YYYY, so PeriodParser is what declines this one")]
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
        // TOTALITY: the reduction is TOTAL, and the identity holds VACUOUSLY here — no match
        // exists, so nothing is stripped and the line is already empty. DECLARED UNREACHABLE from
        // every reader (CLAUDE.md §5), which is why the value is not a claim about production. No call site
        // can produce it: HeadingDrivenResumeSegmenter's entries carry only non-blank lines, and
        // ReviewText.DescriptionLines filters `l.Length > 0` before the period test runs. It is
        // pinned because the method is shared Infrastructure API that must not throw on the
        // degenerate input, NOT as a claim about what production does with an empty line.
        ShouldReduceTo(string.Empty, string.Empty);
    }
}
