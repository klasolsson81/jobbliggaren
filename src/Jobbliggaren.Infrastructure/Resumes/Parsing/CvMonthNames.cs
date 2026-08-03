namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// The ONE home for "what a Swedish or English month word looks like, and which month it is"
/// (#1060 road 3). Consumed by <see cref="DatePatterns.DateRange"/>, which matches a month point
/// inside a range, and by <see cref="PeriodParser"/>, which parses one into a date — the two
/// spellings of a single grammar that CLAUDE.md §9.1 requires to have one owner.
///
/// <para><b>Why this type exists at all, and it is not tidiness.</b> The widening first landed with
/// the month alternation written twice inside one regex literal and <see cref="PeriodParser"/>
/// untouched. The result was measured: a period that parsed before the widening
/// ("jan 2020 – nuvarande" → <c>Period = "2020 – nuvarande"</c>) stopped parsing after it, because
/// <c>ExtractPeriod</c> stores <see cref="DatePatterns.DateRange"/>'s match VALUE and
/// <see cref="PeriodParser"/> is what consumes that value. Two spellings of one grammar cannot be
/// widened one at a time; the intermediate state is a regression (senior-cto-advisor re-bind
/// 2026-08-03, Approach A). A third copy of this list — in either consumer — reintroduces exactly
/// that failure mode and is forbidden.</para>
///
/// <para><b>The ordering contract is PREFIX-ORDER, not length-order, and the distinction is
/// checkable.</b> .NET alternation is ordered: the first branch that lets the overall match succeed
/// wins. For literal alternatives that only matters where one is a PREFIX of another, so the
/// invariant is <b>no alternative may precede an alternative it is a prefix of</b> — "januari"
/// before "jan", "september" before "sept" before "sep", "februari" before "febr" before "feb".
/// Length-order is sufficient but not necessary, and stating it as the contract makes the rule
/// unverifiable by inspection: this list has one length inversion ("februari" before "september")
/// and zero prefix inversions. <c>CvMonthNamesTests</c> checks the prefix invariant mechanically.
/// </para>
///
/// <para><b>Not a knowledge-bank asset (CLAUDE.md §5).</b> §5 bans hardcoded rubric thresholds,
/// cliché lists and action-verb lists — all of which are editorial judgement, revisable without a
/// code change and owned by the product. A month name is a lexical fact about a language, on the
/// same footing as <see cref="PeriodParser"/>'s present-keywords and <see cref="DatePatterns.Year"/>'s
/// century list. Loading it as data would also force a runtime <c>new Regex(...)</c>, losing the
/// source generator and putting externally-editable text into a regex.</para>
/// </summary>
internal static class CvMonthNames
{
    /// <summary>
    /// The month-word alternation, prefix-ordered, as a non-capturing group. A compile-time
    /// constant so both consumers keep <c>[GeneratedRegex]</c> and its source generator.
    /// </summary>
    public const string Pattern =
        "(?:januari|january|februari|february|september|november|december|augusti|oktober|"
        + "october|august|march|april|sept|mars|juni|june|juli|july|febr|maj|may|"
        + "jan|feb|mar|apr|jun|jul|aug|sep|okt|oct|nov|dec)";

    /// <summary>
    /// What may sit between the month word and its year: an optional abbreviating period, then
    /// HORIZONTAL whitespace only.
    ///
    /// <para><b><c>[^\S\r\n]</c> rather than <c>\s</c>, and that is a defect fix rather than a
    /// style choice.</b> <c>\s</c> matches <c>\n</c>, and
    /// <c>HeadingDrivenResumeSegmenter.ExtractPeriod</c> matches against <c>entry.Text</c> — the
    /// entry's lines joined with <c>'\n'</c>, not a single line. With <c>\s+</c> a prose bullet
    /// ending in a month word was absorbed into the match: measured through the real segmenter,
    /// <c>Period = "maj\n2020 – 2024"</c>, a newline and a word lifted out of a description and
    /// ridden into the promoted CV on the auto-promote path, which has no approve step. A month
    /// point is a LINE-LOCAL grammar and this token is what says so — shared, so neither consumer
    /// can inherit the defect the other fixed.</para>
    /// </summary>
    public const string AfterName = @"\.?[^\S\r\n]+";

    // Every alternative in Pattern maps here, and nothing else does. CvMonthNamesTests asserts the
    // correspondence in BOTH directions — an alternative with no entry parses to a wrong month
    // silently, and an entry with no alternative is a token production can never deliver.
    // Ordinal-not-Swedish: the key set is a lexical index, so the comparer is OrdinalIgnoreCase to
    // match the regex's own IgnoreCase, never a culture-sensitive one.
    private static readonly Dictionary<string, int> Ordinals =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["januari"] = 1,
            ["january"] = 1,
            ["jan"] = 1,
            ["februari"] = 2,
            ["february"] = 2,
            ["febr"] = 2,
            ["feb"] = 2,
            ["mars"] = 3,
            ["march"] = 3,
            ["mar"] = 3,
            ["april"] = 4,
            ["apr"] = 4,
            ["maj"] = 5,
            ["may"] = 5,
            ["juni"] = 6,
            ["june"] = 6,
            ["jun"] = 6,
            ["juli"] = 7,
            ["july"] = 7,
            ["jul"] = 7,
            ["augusti"] = 8,
            ["august"] = 8,
            ["aug"] = 8,
            ["september"] = 9,
            ["sept"] = 9,
            ["sep"] = 9,
            ["oktober"] = 10,
            ["october"] = 10,
            ["okt"] = 10,
            ["oct"] = 10,
            ["november"] = 11,
            ["nov"] = 11,
            ["december"] = 12,
            ["dec"] = 12,
        };

    /// <summary>The token set, for the correspondence test. Not read by production.</summary>
    public static IReadOnlyCollection<string> KnownTokens => Ordinals.Keys;

    /// <summary>
    /// Resolves a month word to its 1-12 ordinal. <paramref name="token"/> is the raw match, so a
    /// trailing abbreviating period ("dec.") is tolerated here rather than in every caller.
    /// </summary>
    public static bool TryGetOrdinal(string token, out int month)
    {
        var trimmed = token.TrimEnd('.');
        return Ordinals.TryGetValue(trimmed, out month);
    }
}
