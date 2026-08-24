using System.Text.RegularExpressions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Deterministic CV date-token patterns shared across parsing (F4-8) and the review engine
/// (F4-9). Promoted to this neutral <c>Infrastructure/Resumes/Parsing</c> home so the ONE
/// knowledge piece — "what a CV date range / bare year looks like" — has a single owner
/// (DRY, CLAUDE.md §9.1; parity with <see cref="PeriodParser"/>, promoted here for the same
/// reason). <see cref="HeadingDrivenResumeSegmenter"/> matches these to extract/strip a
/// period from an entry; <see cref="Review.Rules.ReviewText"/> masks them so an employment
/// date is never miscounted as a measurable result (#487). The patterns are word-bounded
/// (mid-text), NOT anchored — contrast <see cref="PeriodParser"/>, which anchors <c>^…$</c>
/// for whole-string parsing.
/// </summary>
internal static partial class DatePatterns
{
    // A date RANGE: a start point (YYYY, MM/YYYY or ISO YYYY-MM) — dash — an end point or a
    // present-keyword.
    //
    // THE ORDERING CONTRACT IS PREFIX-ORDER: no alternative may precede an alternative it is a
    // PREFIX of. That is the invariant the code holds and it is checkable by inspection. An earlier
    // revision stated it as "longest-alternative-first", which is sufficient but not necessary and
    // is literally false of this list — CvMonthNames writes "februari" (8) before "september" (9)
    // and has ZERO prefix inversions. Stating the unverifiable version re-creates exactly the
    // problem commit 1 fixed: a reader auditing the list finds the stated rule violated, and either
    // "fixes" it for nothing or stops trusting the contract.
    //
    // .NET's alternation is ordered and non-greedy across branches: the first branch that lets the
    // OVERALL match succeed wins, and nothing forces backtracking to a longer one afterwards. With
    // the bare `\d{4}` written first, "2020-06 – 2024-03" matched only as far as "2024" — the `\b`
    // after it holds against the following "-", so the overall match SUCCEEDS and the end month is
    // dropped. That is why `\d{4}` must come last among the digit forms, and it reached three
    // surfaces at once (IsDateOnlyLine, ExtractPeriod's stored VALUE, StripDates' masking), so it
    // was one ordering defect and not three bugs.
    //
    // ORDER ONLY BITES WHERE A SHORT BRANCH CAN SUCCEED, and that is the distinction the earlier
    // revision missed. Where a too-short branch makes the overall match FAIL, .NET backtracks into
    // the longer one and no defect is possible: that is why the START alternation never showed the
    // ISO defect, and it is also why the month list could not have shown one — every month branch
    // is followed by `\.?[^\S\r\n]+\d{4}`, which "jan" against "januari 2020" cannot satisfy (the
    // whitespace token falls on the "u"), so the engine backtracks. A previous revision of this
    // comment claimed the month ordering was load-bearing; it is not, and it is kept as defence in
    // depth at zero cost rather than as a defect averted. The digit-form ordering IS load-bearing
    // and is pinned twice.
    //
    // WHY THE CORRECTION LANDED BEFORE ANY ALTERNATIVE WAS ADDED (senior-cto-advisor re-bind
    // 2026-08-02, bind 9): a trailing-qualifier branch placed after `\d{4}` would make
    // "2020 – 2024 (heltid)" match "2024" and leave the qualifier as a tail — a short branch that
    // succeeds, which is the class that bites.
    // THE POINT FORMS, and each is a modelling decision rather than a pattern that grew:
    //   MONTHNAME YYYY   "jan 2020", "januari 2020", "Dec. 2024"  (Swedish and English, IgnoreCase)
    //   YYYY-MM          ISO 8601
    //   MM/YYYY          the slash-written month-first form
    //   YYYY             a bare year
    //
    // YYYY/MM (the slash-written YEAR-FIRST form, "2020/01") is DELIBERATELY NOT a point form in
    // THIS alternation — it IS one in the row grammar below, and keeping the two apart is ADR 0136.
    // Road 3 briefly modelled it here (commit 2) and then un-modelled it (round 5) — see the note
    // below and DateRangeYearFirstCharacterisationTests for why: it collides with the Swedish läsår
    // notation and no home has authority to read it as a month.
    //
    // THE POINT FRAGMENT IS SHARED WITH PeriodParser AND THAT IS LOAD-BEARING, not tidiness. This
    // regex's match VALUE is what HeadingDrivenResumeSegmenter.ExtractPeriod STORES as
    // ParsedExperience.Period, and PeriodParser is what CONSUMES that value. Widening one and not
    // the other produced a measured regression: "jan 2020 – nuvarande" stored "2020 – nuvarande"
    // (parseable) before the widening and "jan 2020 – nuvarande" (refused) after, which cost
    // A4/B6/B7 their verdicts AND cost OccupationExperienceDeriver the entry's years entirely.
    // The month list therefore lives in CvMonthNames, once (senior-cto-advisor re-bind 2026-08-03,
    // Approach A). A copy of it here would recreate the defect.
    //
    // A month NAME point is deliberately only recognised INSIDE a range HERE. A lone "maj 2020" is
    // not reduced (Year() takes the "2020" and leaves "maj"), exactly as a lone "03/2020" is not —
    // the range separator is what disambiguates a period from a date mentioned in prose, and #428
    // already settled that a lone bare year on a non-header line must NOT be read as a period.
    // PeriodParser DOES accept the lone point, as it already did for "2020" and "03/2020"; the two
    // types disagree there on purpose and IsDateOnlyLine's docblock records it as a standing axis.
    // THE END ALTERNATION VALIDATES THE MONTH STRUCTURALLY; THE START ALTERNATION DOES NOT, AND THE
    // ASYMMETRY IS THE CONTRACT (senior-cto-advisor R3 bind 2026-08-03, correcting that bind's own
    // earlier §1.2).
    //
    // The completed rule: prefix-order is NECESSARY BUT NOT SUFFICIENT, and structural exactness
    // completes it EXACTLY WHERE ORDER BITES — the END alternation, because a short branch can
    // succeed there and cannot in the START. An earlier revision said "every alternative it orders
    // first", applied the completion to both alternations, and thereby removed a backtracking rescue
    // the start position depended on. That is the same shape as the retraction eight lines above
    // about the month list: a rule stated more widely than the mechanism it describes.
    //
    // "2018 – 2019-20" is an academic year; in the END position, with the loose `\d{2}`, the
    // month-bearing branch won and the whole line was stored and then refused, where before the
    // bare-year branch won and it degraded to a parseable "2018 – 2019". In the START position no
    // such thing happens — `\d{4}` forces the separator to eat the "-", the end point fails, and the
    // engine backtracks — so the exact class bought nothing there and cost a match.
    //
    // A NOTE ON A CLAIM THAT WAS FALSE HERE. This comment read: the academic year's "last two digits
    // lie outside 01-12 BY CONSTRUCTION". They do not. A läsår is YYYY/YY where YY = (YYYY+1) mod
    // 100, which lands INSIDE 01-12 for twelve start-years, 2000/01 through 2011/12. The HYPHEN form
    // reads those twelve as a month on ISO 8601's authority, and that stands. The SLASH form briefly
    // read them as a month too (commit 2) on no authority at all, and a round-5 measurement found the
    // asymmetric case that made it a Blocker: a slash point paired with an UNRELATED endpoint (e.g.
    // "2020 – 2024/12") stored a value the month reading then made unparseable, which origin/main had
    // stored as a working bare-year degradation. Round 5 removed the slash branch from BOTH point
    // lists, so YYYY/MM is now read as a month by NEITHER home — origin/main's behaviour, restored
    // rather than repaired. See DateRangeYearFirstCharacterisationTests, which pins the collision, the
    // per-commit attribution, and this resolution. The form IS recognised as a date ROW, by
    // DateRowRange below and by nothing in this alternation — ADR 0136 owns why the two are separate.
    //
    // `\d{2}/\d{4}` IS DELIBERATELY NOT NARROWED, and the reason is the contract, not convenience.
    // It stands in no prefix relation to any other alternative — strings matching it open with two
    // digits and a slash — so the rule's premise never reached it. "13/2020 – 2024" is therefore not
    // a wrong branch beating a right one; it is a form NO branch models, where DateRange matches a
    // sub-span. Narrowing it would leave a "13/" residue instead of degrading, which flips
    // IsDateOnlyLine false and hands the date row back to the Organization slot — the β-3 class this
    // lane just closed. It stays as a documented axis with its own frozen pin.
    [GeneratedRegex(RangeOpen + StartPoint + RangeMiddle + EndPoint + RangeClose, RangeOptions)]
    public static partial Regex DateRange();

    // THE FOUR POINT LISTS ARE A 2x2 OVER TWO ONE-TOKEN DELTAS, AND THE DELTAS ARE THE CONTRACT:
    // {START, END} x {value grammar, row grammar}, differing on the hyphen month class and on the
    // slash point. `DateRangeYearFirstCharacterisationTests.TheFourPointLists_…` asserts all four
    // byte-identical apart from those two substitutions, so no divergence can widen silently and a
    // future alternative added to the shared fragments lands in all four.
    //
    // WHY THE START LIST KEEPS THE LOOSE `\d{4}-\d{2}` WHILE THE END LIST DOES NOT. Structural
    // exactness exists to COMPLETE prefix-order, so it is required exactly where prefix-order
    // bites — and this file already said where that is: "ORDER ONLY BITES WHERE A SHORT BRANCH CAN
    // SUCCEED … that is why the START alternation never showed the ISO defect." In the END list the
    // short `\d{4}` can complete the overall match (the `\b` holds against the following "-"), so an
    // inexact month class lets a wrong branch win. In the START list it cannot: `\d{4}` forces the
    // separator to eat the "-", the end point then fails, and the engine backtracks into the longer
    // branch. Applying the completion to a rule that never reached the start position removed a
    // backtracking rescue that was load-bearing in the BENEFICIAL direction — measured:
    // "2019-20 – 2021" (an academic year) went from a matched, correctly-suppressed date row to no
    // match at all, which handed the row back to the Organization slot (β-3) and, on the Lines[0]
    // layout, turned an honest refusal into a confident "2019" (senior-cto-advisor R3 bind
    // 2026-08-03).
    //
    // The two consumers want OPPOSITE postures and that is why one list cannot serve both: the LINE
    // question (IsDateOnlyLine → suppression) wants maximal structural coverage, because an
    // ambiguous date is still a date; the VALUE question (ExtractPeriod → stored Period →
    // PeriodParser) wants exactness, because an ambiguous date stored as a confident claim is worse
    // than none. origin/main served both by accident — loose structure plus PeriodParser's semantic
    // guard. This restores that separation by POSITION, which is where the mechanism differs.
    //
    // The language delta is derivable rather than grepped: strings matching `\d{4}-\d{2}` but not
    // `\d{4}-(?:0[1-9]|1[0-2])` — i.e. YYYY-NN with NN in {00, 13..99} — in START position only.
    // Nothing else can move, because no other alternative's language changes.
    //
    // THE CONSEQUENCE HALF, which the derivation above does not state and which is the part
    // worth knowing: the restore is a STRICTLY MONOTONE widening — a loose-branch match and the
    // `\d{4}` fallthrough can never both succeed at one index, so no match shortens and none is
    // lost. But ExtractPeriod runs DateRange leftmost over the WHOLE entry text, so a new match
    // arising to the LEFT can shadow one further right. That direction is favourable here (the
    // date row is left of the bullets on every layout this lane models), and it is the reason the
    // narrowing was worse than it looked: at commit 4 an entry whose date row stopped matching
    // could store an interval lifted out of a DESCRIPTION bullet as its Period.
    private const string SharedPointHead =
        CvMonthNames.Pattern + CvMonthNames.AfterName + @"\d{4}";

    private const string RangeOpen = @"\b(";

    private const string RangeMiddle = @")\s*[-–—]\s*(";

    private const string RangeClose = "|" + CvMonthNames.PresentKeywords + @")\b";

    private const RegexOptions RangeOptions = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private const string PointOpen = "(?:";

    private const string LooseHyphenPoint = @"|\d{4}-\d{2}";

    private const string ExactHyphenPoint = @"|\d{4}-(?:0[1-9]|1[0-2])";

    // The year-first SLASH point, and it is a member of the LINE lists ONLY (ADR 0136).
    private const string SlashPoint = @"|\d{4}/\d{2}";

    private const string SharedPointTailHead = @"|\d{2}/\d{4}";

    private const string SharedPointTailFoot = @"|\d{4})";

    private const string SharedPointTail = SharedPointTailHead + SharedPointTailFoot;

    // Prefix-order: SlashPoint precedes the bare `\d{4}` it is a prefix of, per the contract above.
    private const string LinePointTail = SharedPointTailHead + SlashPoint + SharedPointTailFoot;

    private const string StartPoint = PointOpen + SharedPointHead + LooseHyphenPoint + SharedPointTail;

    private const string EndPoint = PointOpen + SharedPointHead + ExactHyphenPoint + SharedPointTail;

    private const string LineStartPoint = PointOpen + SharedPointHead + LooseHyphenPoint + LinePointTail;

    private const string LineEndPoint = PointOpen + SharedPointHead + ExactHyphenPoint + LinePointTail;

    // THE ROW GRAMMAR. Same shape as DateRange, one point form wider, and it NEVER produces a
    // stored value — read by StripTrailingDate, StripDates and IsUnreadableDateRow, all of which
    // answer a LINE or MASK question. ADR 0136 owns why the two grammars are separate.
    //
    // THE RANGE SKELETON IS SHARED WITH DateRange AND THAT IS THE SYNC MECHANISM, not tidiness.
    // The two grammars must stay in the relation "row recognises everything value recognises, plus
    // the slash point"; only the POINT LISTS may differ, and TheFourPointLists_… pins that they
    // differ by exactly the two permitted deltas. It cannot see the skeleton, so a skeleton written
    // twice could diverge with that test green — widening DateRange's separator class alone
    // ("till"/"to", which PeriodParser already accepts) would break the superset silently and
    // reopen beta-3. Composing both from one set of constants removes the second copy instead of
    // pinning it.
    [GeneratedRegex(RangeOpen + LineStartPoint + RangeMiddle + LineEndPoint + RangeClose, RangeOptions)]
    private static partial Regex DateRowRange();

    // Exposed for the delta correspondence test only. The four lists are a 2x2 over two orthogonal
    // one-token deltas, and a contract nothing can read is a comment;
    // DateRangeYearFirstCharacterisationTests asserts both deltas in both directions.
    internal const string StartPointForTests = StartPoint;

    internal const string EndPointForTests = EndPoint;

    internal const string LineStartPointForTests = LineStartPoint;

    internal const string LineEndPointForTests = LineEndPoint;

    // A bare four-digit year 1900–2099.
    [GeneratedRegex(@"\b(19|20)\d{2}\b", RegexOptions.CultureInvariant)]
    public static partial Regex Year();

    /// <summary>
    /// Replaces every date range and bare year in <paramref name="text"/> with a space, so a
    /// downstream digit test cannot mistake an employment date for a quantified result (#487).
    /// Ranges are masked before bare years so a range's inner years are consumed with the range.
    ///
    /// <para>Masking is a LINE/MASK question, never a VALUE one — nothing here is stored — so it
    /// reads the ROW grammar, the same one <see cref="StripTrailingDate"/> reads (ADR 0136). Before
    /// #1195 it read <see cref="DateRange"/>, which left a year-first slash range's digits unmasked
    /// inside a prose bullet: the same §5 cited-evidence inversion the line-level suppression
    /// closes, one altitude down. The row grammar's year class is unbounded, so a bullet whose only
    /// digits are <c>NNNN/NN – NNNN</c> masks whole whatever those digits mean — a priced widening
    /// of the residual the bare <c>\d{4} – \d{4}</c> already carries; read the <c>InlineData</c> in
    /// <c>DateRangeYearFirstCharacterisationTests.StripDates_MasksASlashRangeInsideProse</c>.</para>
    /// </summary>
    public static string StripDates(string text) =>
        Year().Replace(DateRowRange().Replace(text, " "), " ");

    /// <summary>
    /// Removes a TRAILING date range or bare year from <paramref name="line"/>, together with the
    /// separators left dangling behind it. Only strips when the match runs to the END of the line:
    /// a leading or internal year is likely part of a name ("Studio 2005 Design") and is left alone.
    /// Returns <paramref name="line"/> unchanged when no match reaches the end.
    ///
    /// <para>Promoted here from <see cref="HeadingDrivenResumeSegmenter"/> (#1060 β-3 follow-up) so
    /// that <see cref="IsDateOnlyLine"/> can be defined AS this reduction rather than as a second
    /// copy of it. Behaviour is byte-identical to the segmenter's former private method.</para>
    /// </summary>
    public static string StripTrailingDate(string line)
    {
        var range = DateRowRange().Match(line);
        if (range.Success && IsIgnorableTail(line[(range.Index + range.Length)..], allowQualifier: true))
            return TrimTrailingSeparators(line[..range.Index]);

        var year = Year().Match(line);
        if (year.Success && IsIgnorableTail(line[(year.Index + year.Length)..], allowQualifier: false))
            return TrimTrailingSeparators(line[..year.Index]);

        return line;
    }

    // What may follow the match and still leave the line "nothing but a date".
    //
    // TWO OF THE THREE SURVIVING WIDENED FORMS LIVE HERE AND NOT IN DateRange, AND THE SPLIT IS THE
    // DESIGN (a fourth, YYYY/MM, was tried in DateRange and taken back out — see the note above).
    // DateRange's match VALUE is what ExtractPeriod stores as ParsedExperience.Period, so anything
    // added there rides into the promoted CV and must survive PeriodParser. A trailing qualifier
    // does not: modelling "2020 – 2024 (heltid)" inside DateRange would store
    // Period = "2020 – 2024 (heltid)", which PeriodParser refuses — turning a period that parses
    // today into one that does not, and losing A4/B6/B7 on an entry that currently has them. That
    // is a REGRESSION dressed as a widening. Asking instead "does anything but a date follow?" adds
    // the suppression without touching the stored value: Period stays "2020 – 2024".
    //
    // The keyword-less open end ("2020 –") is here for the second half of the same reason. An
    // empty-end alternative in DateRange would match mid-prose ("Acme AB 2020 - Systemutvecklare"),
    // widening the MASK far beyond the line-level question actually being asked.
    //
    // THE ACCEPTED SET IS DELIBERATELY NARROWER THAN TrimTrailingSeparators', and the honest
    // statement of the rule is about what a glyph CAN BEGIN, not about what follows it. The
    // right-hand set is restricted to glyphs that cannot begin a field — whitespace and range
    // separators — plus a self-delimiting parenthesised group. A lone trailing "," / ";" / "|" is
    // therefore KNOWINGLY left on the non-date-only side even though nothing follows it, so
    // "2005 - 2010," is still not a date-only line and, on the two-line layout, still becomes the
    // Organization. That residual is priced, not overlooked.
    //
    // The alternative not taken: apply TrimTrailingSeparators' own set to the tail and require the
    // RESIDUE to be empty — which would reject ", Acme AB" on the residue rather than on the glyph
    // and would close the residual. It moves a frozen pin, so it is a separate change-reason.
    //
    // THE PARENTHESISED QUALIFIER IS ACCEPTED AFTER A RANGE ONLY (allowQualifier), never after a
    // bare YEAR. A bare year is the weaker date signal — this file's own position, stated in
    // StripTrailingDate's docblock as why "Studio 2005 Design" is left alone — so licensing a
    // bracket strip after one contradicts it: "Studio 2005 (Design)" would lose its tail while its
    // pinned twin one glyph away does not, and "Acme AB 2000 (publ)" would silently drop the
    // standard suffix of a Swedish public limited company. Parentheses only; square brackets are
    // not accepted, and the group must span the whole tail with no inner bracket of either kind, so
    // "(a)(b)", "(a) x" and "Acme AB (heltid)" are all rejected (senior-cto-advisor re-bind
    // 2026-08-03 §5, which measured that the restriction moves no pinned row).
    private static bool IsIgnorableTail(string tail, bool allowQualifier)
    {
        var rest = tail.Trim();
        if (rest.Length == 0)
            return true;

        // A dangling range separator, with or without a qualifier behind it. This half is accepted
        // on BOTH branches: it is what makes the keyword-less open end ("2020 –") a date-only line,
        // and there the match comes from Year(), not from DateRange().
        rest = rest.TrimStart(' ', '\t', '-', '–', '—').TrimStart();
        if (rest.Length == 0)
            return true;

        if (!allowQualifier || rest.Length <= 1 || rest[0] != '(' || rest[^1] != ')')
            return false;

        var inner = rest[1..^1];
        return !inner.Contains('(') && !inner.Contains(')');
    }

    /// <summary>
    /// True when a date match runs to the END of <paramref name="line"/> and nothing but separators
    /// precedes it — "the line carries a date and nothing else". <b>What may FOLLOW the match is
    /// whitespace, a dangling range separator, or a single parenthesised qualifier — and nothing
    /// else</b>, so <c>"2005 - 2010,"</c> is still false while <c>"2020 –"</c> and
    /// <c>"2020 – 2024 (heltid)"</c> are true; <see cref="IsIgnorableTail"/> owns that set and the
    /// reason it is narrower than the trim applied to the LEFT of the match. Also true VACUOUSLY for
    /// the empty line, which carries no match at all — declared unreachable from every call site
    /// and pinned as such, not a claim about what production does with one. This is
    /// <see cref="StripTrailingDate"/> asked as a question, deliberately not a second
    /// implementation: one knowledge piece, one home (DRY, CLAUDE.md §9.1), which is the same move
    /// that gave <see cref="DatePatterns"/> and <see cref="PeriodParser"/> their neutral home.
    ///
    /// <para><b>Two readers, and they must agree</b> — three call sites across them: the segmenter
    /// reads the reduction in <c>StripTrailingPeriod</c> and this predicate directly in
    /// <c>SplitTitleOrganization</c>. The segmenter asks it to refuse a field —
    /// a line carrying no field must not BECOME one (#1060 β-3) — and
    /// <see cref="Review.Rules.ReviewText.DescriptionLines"/> asks it to refuse a bullet, so the
    /// review engine never scores the user's date row as prose. Before the promotion those two
    /// agreed only by accident: the review side suppressed such a line via its
    /// organization-equality test, which fired only BECAUSE the segmenter had fabricated the line
    /// into the organization slot. β-3 stopped that fabrication and the accident with it.</para>
    ///
    /// <para><b>It does not subsume <see cref="PeriodParser"/>, and neither subsumes the other.</b>
    /// <b>THE AXES BELOW ARE KNOWN INSTANCES, NOT AN EXHAUSTIVE COUNT, and this docblock publishes
    /// no total on purpose.</b> The adjudicator is
    /// <c>DatePatternsDateOnlyLineTests</c> — read the `InlineData` there, not a number here. An
    /// earlier revision said "three axes", the next hardened it to "four", and both were wrong: the
    /// count is an emergent property of two independently written grammars, so any total is a claim
    /// that decays the moment either changes. Road 3 changed one of them, and the lists below moved
    /// with it — in BOTH directions, which is why no total survives the move.</para>
    ///
    /// <para><b>What road 3 added.</b> The date model now reaches month names
    /// ("jan 2020 – dec 2024"), a trailing parenthesised qualifier ("2020 – 2024 (heltid)") and a
    /// keyword-less open end ("2020 –"). The first is a POINT form and lives in
    /// <see cref="DateRange"/>; the other two are properties of the LINE and live in
    /// <see cref="IsIgnorableTail"/>, because <see cref="DateRange"/>'s match value is what
    /// <c>ExtractPeriod</c> stores and a qualifier inside it would break the stored period rather
    /// than widen it. <b>A LONE month point ("maj 2020") is still not reduced HERE</b> — only ranges
    /// are — exactly as a lone "03/2020" is not (#428). <see cref="PeriodParser"/> does accept the
    /// lone point, as it already did for "2020"; that disagreement is deliberate and is one of the
    /// standing axes above, not a deferral.</para>
    ///
    /// <para><b>A fourth form, <c>YYYY/MM</c> ("2020/01"), reaches this predicate WITHOUT being a
    /// point form in <see cref="DateRange"/>, and that is the whole of ADR 0136.</b> It is not a
    /// point form there, on either endpoint, for the same reason <see cref="PeriodParser"/> never
    /// dates it (Klas-direktiv 2026-08-03): the year-first slash notation is how Swedish writes a
    /// YEAR PAIR — a läsår or a räkenskapsår — and no home has authority to read it as a month. The
    /// LINE question is a different question, so it gets a different grammar: this predicate reads
    /// <c>DateRowRange</c>, whose match value is stored by nobody. See
    /// <c>DateRangeYearFirstCharacterisationTests</c> for the collision and the per-commit
    /// attribution.</para>
    ///
    /// <para><b>A MONTH WORD THAT IS ALSO A NAME COSTS A REAL ORGANIZATION, and it is priced here
    /// rather than guarded.</b> "Mars 2020 – 2024" and "Maj 2018 – 2020" reduce to empty, so the line
    /// is date-only and <c>SplitTitleOrganization</c> nulls the organization — but <i>Mars</i> is a
    /// real employer (Mars Sverige AB) and <i>Maj</i> and <i>Juni</i> are Swedish given names. This is
    /// the INVERSE of the class β-3 closed: not asserting a field the source never wrote, but
    /// dropping one it did. Accepted rather than guarded on two grounds — the shape required is
    /// narrow (the line must be exactly <c>MonthWord YYYY – endpoint</c>, so an employer line with
    /// anything else on it is unaffected), and β-3's own framing is that dropping is the lesser
    /// failure: <i>"the engine did not DROP a field. It ASSERTED one."</i> Named so a later reader
    /// meets the cost rather than rediscovering it (dotnet-architect R11, 2026-08-03).</para>
    ///
    /// <para><b>A DEFERRAL WAS TAKEN HERE AND THEN OVERTURNED BY MEASUREMENT — kept, because the
    /// prediction it made is the one that came FALSE.</b> This paragraph read: <i>"Nor did
    /// PeriodParser move … those forms now yield a Period that is EXTRACTED but not PARSEABLE. That
    /// is benign and was verified rather than assumed — CvReviewEngine falls to
    /// DatedExperience(experience, null, null, null), identical to an absent period, so A4/B6/B7
    /// report the same NotAssessed they reported <b>when the value was null</b>. Teaching
    /// PeriodParser these forms would ASSESS them instead, which is a different change-reason and a
    /// follow-up."</i> The final clause is the load-bearing one and it is the tell: the verification
    /// covered only the shapes whose value <b>was</b> null. For the ASYMMETRIC shapes — a month name
    /// on one side, a parseable point or a present-keyword on the other — the
    /// value was not null and it DID parse. Measured in both polarities:
    /// "jan 2020 – nuvarande" stored <c>"2020 – nuvarande"</c> (parseable, years attributed
    /// 2020..2026) and became <c>"jan 2020 – nuvarande"</c> (refused, years lost). A sentence true
    /// of its evidence and false of its subject — the evidence being the four forms the change was
    /// written for, none of which regressed. <see cref="PeriodParser"/> now moves with this type
    /// (senior-cto-advisor re-bind 2026-08-03, Approach A), and the coverage that deferral called a
    /// separate change-reason turns out to be inseparable from the invariant: one
    /// <c>PointRegex</c> branch does both, so restoring the one necessarily gains the other.
    /// <b>One of the four original forms, <c>YYYY/MM</c>, was later carved back OUT of this
    /// invariant</b> (round 5): its point branch produced the identical asymmetric failure one
    /// altitude up — an unreadable value stored beside a perfectly readable sibling endpoint — so
    /// the type-pair now agrees on it by both declining, not by both accepting. The invariant holds;
    /// what moved is which forms are inside it.</para>
    ///
    /// <para><c>PeriodParser</c> is wider at least on: the word separators "till"/"to";
    /// single-digit months (<c>\d{1,2}</c> against this type's <c>\d{2}</c>); "." as a month
    /// separator where this type takes only "/"; "-" as a month separator <b>in the RIGHT point of
    /// a range</b> ("2020 – 03-2024") but NOT in a lone or left point ("03-2020"), because
    /// <c>SeparatorRegex</c> splits on the first separator IT ACCEPTS and <c>Split(trimmed, 2)</c>
    /// never splits the right part again — so a hyphen in a position that regex accepts as a split
    /// is consumed as one, and its
    /// "03" fails the point match, while a hyphen to the right of an accepted split reaches
    /// <c>PointRegex</c>'s <c>[/.\-]</c> branch intact;
    /// and a lone date POINT with no range separator ("03/2020"), which <see cref="DateRange"/>
    /// cannot match at all and <see cref="Year"/> reduces only to "03/", since "/" is not in the
    /// trailing-separator set.</para>
    ///
    /// <para>This predicate is wider at least on: a LEADING separator ("– 2020 – 2024"), which
    /// `PeriodParser`'s <c>^…$</c> anchoring refuses; and a range whose month is out of range
    /// ("13/2020 – 2024"), which <see cref="DateRange"/> matches structurally while `PeriodParser`
    /// validates the month and declines. So the callers take their UNION — substituting either for
    /// the other narrows suppression in one direction or the other.</para>
    ///
    /// <para><b>The ISO end-point axis WAS an alternation-ordering defect, and it is CORRECTED
    /// (#1060 road 3, commit 1).</b> It is recorded here rather than deleted because the shape
    /// recurs: the ordering inside <see cref="DateRange"/> reached every surface that reads the
    /// match rather than the predicate, so one token of order was three defects.
    /// <c>HeadingDrivenResumeSegmenter.ExtractPeriod</c> returns the match VALUE, so
    /// "2020-06 – 2024-03" was stored as <c>Period = "2020-06 – 2024"</c> — the end month dropped
    /// from the value that rides into the promoted CV, on a path with no approve step — and
    /// <see cref="StripDates"/> left "-03" unmasked, so a prose bullet carrying an inline ISO range
    /// read as carrying a measurable digit. Downstream, the truncated value degraded
    /// <c>PeriodParser</c>'s format token from <c>MM/YYYY</c> to <c>YYYY</c>, which beside a
    /// slash-formatted entry produced a false "Blandade datumformat" Warn from B6 on a CV that is
    /// consistent at month granularity — the defect #420 exists to prevent. All four were measured
    /// before the correction and re-measured after; the ordering rationale lives on
    /// <see cref="DateRange"/> itself, and <c>DatePatternsAlternationOrderingTests</c> is the
    /// adjudicator.</para>
    /// </summary>
    public static bool IsDateOnlyLine(string line) => StripTrailingDate(line).Length == 0;

    /// <summary>
    /// True when <paramref name="line"/> carries a date RANGE and nothing else, and the VALUE
    /// grammar (<see cref="DateRange"/>) cannot read it — i.e. the CV states a period this type
    /// recognises as a period but has no authority to date (ADR 0136; today exactly the year-first
    /// slash notation, Klas-direktiv 2026-08-03).
    ///
    /// <para><b>This is the segmenter's ONE question, and the grammar behind it is deliberately not
    /// exposed</b> (ISP): <c>ExtractPeriod</c> needs to know whether the entry states a period it
    /// must decline, not how the row grammar spells one.</para>
    ///
    /// <para><b>Three conjuncts, and each one excludes a population the other two do not.</b> The
    /// RANGE conjunct excludes a line that is date-only only because <see cref="Year"/> reduced it
    /// — <c>"2020 –"</c>, the keyword-less open end, which reaches no range branch and keeps
    /// origin/main's bare-year fallback rather than acquiring a refusal this change was not written
    /// for. The date-ONLY conjunct excludes a range sitting in a line that also carries a field
    /// (<c>"Konsult 2020/01 – 2024/12"</c>) or trailing prose, so the veto cannot reach a line the
    /// suppression itself declines. The <see cref="DateRange"/> conjunct excludes every form the
    /// value grammar CAN read, so a mixed-notation row still stores its bare-year degradation
    /// (<c>"2020 – 2024/12"</c> → <c>"2020 – 2024"</c>) — the round-5 Blocker stays closed.</para>
    /// </summary>
    public static bool IsUnreadableDateRow(string line) =>
        DateRowRange().IsMatch(line) && IsDateOnlyLine(line) && !DateRange().IsMatch(line);

    private static string TrimTrailingSeparators(string value) =>
        value.TrimEnd(' ', '\t', ',', ';', '|', '-', '–', '—');
}
