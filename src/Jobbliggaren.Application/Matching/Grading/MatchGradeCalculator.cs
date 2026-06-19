using Jobbliggaren.Application.Matching.Abstractions;

namespace Jobbliggaren.Application.Matching.Grading;

/// <summary>
/// The deterministic, total preference→<see cref="MatchGrade"/> rule for the F4-13
/// page-scoped match tag (ADR 0076 2026-06-19 graded-ladder re-bind; senior-cto-advisor
/// RB1 + Klas product decision). A pure function over a Fast <see cref="MatchScore"/>'s
/// dimension verdicts — no I/O, no clock, no randomness (equal inputs → equal grade).
/// It is the SSOT for the grade ladder, reusable by F4-14's sort path.
/// <para>
/// <b>The ladder (Fast tier — occupation + region + employment; title is always
/// NotAssessed on the preference path and intentionally not consulted):</b>
/// <list type="number">
/// <item><b>Gate:</b> occupation/SSYK must be a <see cref="MatchDimensionVerdict.Match"/>
/// to earn ANY positive tag — otherwise <c>null</c> (no tag). The Översikt setup nudge,
/// not a discouraging per-card tag, owns the "no occupation stated" case.</item>
/// <item><b>Contradiction floors to <see cref="MatchGrade.Basic"/>:</b> a stated
/// region OR employment preference that the ad contradicts
/// (<see cref="MatchDimensionVerdict.NoMatch"/>) forces the lowest positive grade —
/// Klas 2026-06-19: a deselected location/employment form must not read as a strong
/// match ("det sämsta är ... inte rätt ort").</item>
/// <item><b>Otherwise by confirmed secondaries:</b> both region and employment
/// <c>Match</c> → <see cref="MatchGrade.Strong"/>; exactly one → <see cref="MatchGrade.Good"/>;
/// neither (both <see cref="MatchDimensionVerdict.NotAssessed"/> — "open"/not stated) →
/// <see cref="MatchGrade.Basic"/>. A <c>NotAssessed</c> secondary never penalises (an
/// unstated preference means "open", not "wrong").</item>
/// </list>
/// </para>
/// <para>
/// <b>No opaque total (Goodhart guard):</b> the output is a named category, never a
/// number. The full nine reachable (Region × Employment) verdict tuples each map to
/// exactly one grade — the RED-first table is the oracle. F4-15 re-proves totality by
/// extending the rule with skill/requirement rungs.
/// </para>
/// </summary>
public static class MatchGradeCalculator
{
    /// <summary>
    /// Computes the positive match grade for a Fast <paramref name="score"/>, or
    /// <c>null</c> when the ad does not earn a tag (occupation not a Match). See the
    /// type summary for the full ladder.
    /// </summary>
    public static MatchGrade? Grade(MatchScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        // Gate: occupation/SSYK must Match to show any positive tag.
        if (score.SsykOverlap.Verdict != MatchDimensionVerdict.Match)
            return null;

        // A stated region/employment preference the ad contradicts floors to Basic.
        var regionContradicts = score.RegionFit.Verdict == MatchDimensionVerdict.NoMatch;
        var employmentContradicts = score.EmploymentFit.Verdict == MatchDimensionVerdict.NoMatch;
        if (regionContradicts || employmentContradicts)
            return MatchGrade.Basic;

        // Otherwise grade by the count of confirmed secondary preferences. A NotAssessed
        // secondary ("open"/not stated) neither confirms nor contradicts.
        var confirmedSecondaries = 0;
        if (score.RegionFit.Verdict == MatchDimensionVerdict.Match)
            confirmedSecondaries++;
        if (score.EmploymentFit.Verdict == MatchDimensionVerdict.Match)
            confirmedSecondaries++;

        return confirmedSecondaries switch
        {
            2 => MatchGrade.Strong,
            1 => MatchGrade.Good,
            _ => MatchGrade.Basic,
        };
    }
}
