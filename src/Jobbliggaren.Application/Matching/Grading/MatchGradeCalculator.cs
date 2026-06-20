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

    /// <summary>
    /// The <b>requirement-aware</b> grade overload (ADR 0076 amendment 2026-06-20; Klas
    /// product decision + senior-cto-advisor G1 re-bind). This REVERSES the F4-16
    /// Amendment (b) §1 "skills only extend upward, must-have is evidence-only" rule:
    /// must-have coverage now GATES the upper rungs, so "Stark match"/"Toppmatch" genuinely
    /// means <i>you meet the ad's binding requirements</i>, not merely that your preferences
    /// fit. A pure, total function over the full verdict tuple — no I/O, equal inputs →
    /// equal grade.
    /// <para>
    /// <b>The ladder (SSYK = Match assumed; read top-down):</b>
    /// <list type="number">
    /// <item><b>Gate:</b> <c>SsykOverlap != Match</c> → <c>null</c> (no tag).</item>
    /// <item><b>Contradiction floor (RB1, FIRST):</b> a stated region/employment the ad
    /// contradicts (<c>NoMatch</c>) → <see cref="MatchGrade.Basic"/> — a perfect requirement
    /// match in the wrong city is still Basic (Klas 2026-06-19 "det sämsta är ... fel ort").</item>
    /// <item><b>Requirement gate:</b> <c>mustHaveMet = MustHaveCoverage ∈ {Match, Vacuous}</c>.
    /// <c>Match</c> = all must-haves covered; <c>Vacuous</c> = the ad states none (gate-OPEN,
    /// Klas Reading 1 — a qualified candidate is not punished for a no-requirement ad). A
    /// no-CV user (<c>NotAssessed</c>) or <c>Partial</c>/<c>NoMatch</c> coverage does NOT pass.</item>
    /// <item><b>If the gate is met:</b> both secondaries confirmed AND a skill/nice-to-have
    /// signal (<c>∈ {Match, Partial}</c>) → <see cref="MatchGrade.Top"/>; at least one
    /// confirmed secondary → <see cref="MatchGrade.Strong"/> (the other <c>Match</c>, or
    /// — open-secondary fallback — <c>NotAssessed</c>); no confirmed secondary →
    /// <see cref="MatchGrade.Basic"/>.</item>
    /// <item><b>If the gate is NOT met</b> (no CV / partial / disjoint must-have): a
    /// preference-fit grade, NEVER Strong/Top — at least one confirmed secondary →
    /// <see cref="MatchGrade.Good"/>, else <see cref="MatchGrade.Basic"/>. This is the honest
    /// no-CV ceiling: <c>Good</c> ("Bra match") means "fits your preferences"; uploading a CV
    /// is the only way to reach the requirement-backed rungs.</item>
    /// </list>
    /// </para>
    /// <para>
    /// The Fast <see cref="Grade(MatchScore)"/> overload is unchanged (it has no must-have
    /// input); the VISIBLE grade is this Full overload, computed wherever the full CV skills
    /// are in hand (the page tag + the modal). The F4-14 sort path cannot compute must-have
    /// (top-5 plaintext, no DEK) so it ranks by the Fast band only — honestly DIFFERENT from
    /// this grade in the must-have band (G3-OPT-A, documented + oracle-pinned). Still NO
    /// opaque number anywhere (Goodhart guard) — the gate is a categorical condition on a
    /// categorical verdict, not a magnitude.
    /// </para>
    /// </summary>
    public static MatchGrade? Grade(FullMatchScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var fast = score.Fast;

        // Gate: occupation/SSYK must Match to earn any tag.
        if (fast.SsykOverlap.Verdict != MatchDimensionVerdict.Match)
            return null;

        // Contradiction floor (RB1) — evaluated BEFORE the requirement gate, so a stated
        // region/employment the ad contradicts caps at Basic even with must-haves met.
        if (fast.RegionFit.Verdict == MatchDimensionVerdict.NoMatch
            || fast.EmploymentFit.Verdict == MatchDimensionVerdict.NoMatch)
            return MatchGrade.Basic;

        // Requirement gate: must-have MET (all covered) OR VACUOUS (ad states none, gate
        // -open). NotAssessed (no CV) / Partial / NoMatch do not pass → capped below Strong.
        var mustHaveMet = score.MustHaveCoverage.Verdict
            is MatchDimensionVerdict.Match or MatchDimensionVerdict.Vacuous;

        // NoMatch on a secondary is already floored out above, so a Match here is the only
        // positive; NotAssessed ("open"/not stated) neither confirms nor floors.
        var confirmedSecondaries =
            (fast.RegionFit.Verdict == MatchDimensionVerdict.Match ? 1 : 0)
            + (fast.EmploymentFit.Verdict == MatchDimensionVerdict.Match ? 1 : 0);

        if (mustHaveMet)
        {
            // Top: requirements met + BOTH secondaries confirmed + a positive skill/nice
            // signal. Strong: requirements met + at least one confirmed secondary.
            if (confirmedSecondaries == 2 && HasSkillOrNiceSignal(score))
                return MatchGrade.Top;

            return confirmedSecondaries >= 1 ? MatchGrade.Strong : MatchGrade.Basic;
        }

        // Requirements not met (no CV / partial / disjoint): preference-fit only.
        return confirmedSecondaries >= 1 ? MatchGrade.Good : MatchGrade.Basic;
    }

    // A positive skill OR nice-to-have signal (set overlap). Vacuous is deliberately NOT a
    // signal — "the ad lists no skills" is not evidence of skill fit, so it cannot earn Top.
    private static bool HasSkillOrNiceSignal(FullMatchScore score) =>
        score.SkillOverlap.Verdict is MatchDimensionVerdict.Match or MatchDimensionVerdict.Partial
        || score.NiceToHaveCoverage.Verdict is MatchDimensionVerdict.Match or MatchDimensionVerdict.Partial;
}
