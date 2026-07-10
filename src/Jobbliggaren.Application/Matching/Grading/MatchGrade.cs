using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Matching.Grading;

/// <summary>
/// The page-scoped match TAG grade for the /jobb list card (F4-13, ADR 0076 +
/// 2026-06-19 graded-ladder re-bind; senior-cto-advisor RB1/RB2). A <b>named ordinal
/// CATEGORY</b>, not a number — the visual twin of the F4-3 <c>OccupationMatchKind</c>
/// precedent ADR 0076 Decision 4 itself cites. It is NOT the forbidden opaque total:
/// it has exactly four named members, cannot be "optimized to 92", and the user always sees
/// WHY via the per-dimension verdicts (carried alongside it) + matched/missing in the
/// F4-16 modal. There is deliberately NO numeric/percentage value (Goodhart guard,
/// CLAUDE.md §5 / ADR 0071; ADR 0053 Beslut 5 forbids the percentage ring).
/// <para>
/// <b>Positive-only ladder (Klas product decision 2026-06-19):</b> a grade is only
/// produced when the ad earns a positive tag (occupation/SSYK Match — see
/// <see cref="MatchGradeCalculator"/>). There is no "None"/"NoMatch" member: an ad that
/// does not qualify is simply omitted from the batch result (no tag shown) rather than
/// carrying a negative grade.
/// </para>
/// <para>
/// <b>Requirement-aware ladder (ADR 0076 amendment 2026-06-20):</b> the visible grade now
/// splits on whether the ad's binding <c>must_have</c> requirements are MET.
/// <see cref="Basic"/>/<see cref="Good"/> are <i>preference-fit</i> rungs (occupation +
/// region/employment fit, requirements NOT met — or no CV to assess them);
/// <see cref="Strong"/>/<see cref="Top"/> are <i>requirement-backed</i> rungs (you meet the
/// must-haves and the place/role fit). This reverses F4-16 Amendment (b) §1's "must-have is
/// evidence-only": must-have coverage now GATES the upper rungs, so a no-CV user caps at
/// <see cref="Good"/> and uploading a CV is the only path to <see cref="Strong"/>/<see cref="Top"/>.
/// The full ladder lives in
/// <see cref="MatchGradeCalculator.Grade(Abstractions.FullMatchScore)"/>; the Fast
/// <see cref="MatchGradeCalculator.Grade(Abstractions.MatchScore)"/> overload (no must-have
/// input) is unchanged and still tops at <see cref="Strong"/>.
/// </para>
/// <para>
/// <b>Serialized by NAME, not ordinal (F4-13 is the first surface to put this on the
/// wire):</b> the JSON value is <c>"Basic"</c>/<c>"Good"</c>/<c>"Strong"</c>/<c>"Top"</c>, never a
/// number — self-documenting, reorder-safe, and reinforcing the category-primary intent
/// (a bare ordinal on the wire would read as a score the Goodhart guard forbids).
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchGrade
{
    /// <summary>
    /// The lowest positive grade: the ad matches the user's stated occupation, but no
    /// secondary preference is confirmed — OR a stated region/employment preference is
    /// actively contradicted (a deselected region/employment floors the grade here, per
    /// Klas 2026-06-19: "det sämsta är ... inte rätt ort"). "Grundmatch".
    /// </summary>
    Basic,

    /// <summary>
    /// A <i>related-occupation</i> grade (ADR 0084 §F2, issue #300): the ad's occupation
    /// group is NOT among the user's stated (exact) occupations but IS a substitutable
    /// neighbour of one (the broadened ssyk-4 gate, exact ∪ related). Placed BETWEEN
    /// <see cref="Basic"/> and <see cref="Good"/>: a substitutable occupation in the right
    /// city is a stronger civic signal than the user's exact occupation in the wrong city
    /// (the RB1-floored <see cref="Basic"/>), but it never outranks a positive EXACT-occupation
    /// outcome (<see cref="Good"/>+). v1 is a <b>flat cap</b> — a related match is always
    /// exactly <see cref="Related"/>, regardless of secondary/skill/requirement signals: a
    /// related occupation shares fewer of the occupation's competencies by definition, so
    /// never reaching the skill-backed rungs is precisely how the grade expresses "lower grade
    /// the fewer competencies match" (#300). "Relaterat yrke". Skill-coverage falloff within
    /// the related band is a documented future additive wave (ADR 0084 §C), not v1.
    /// </summary>
    Related,

    /// <summary>
    /// A <i>preference-fit</i> grade: occupation matches and at least one stated
    /// region/employment preference is confirmed, but the ad's must-have requirements are
    /// NOT met — or there is no CV to assess them (the honest no-CV ceiling). "Bra match".
    /// On the Fast overload (no must-have input) this is simply "exactly one secondary
    /// confirmed".
    /// </summary>
    Good,

    /// <summary>
    /// A <i>requirement-backed</i> grade: the ad's must-have requirements are met — or the ad
    /// states none (Vacuous) AND a positive CV skill/nice-to-have signal backs it (F1(b)
    /// amendment 2026-06-27: a Vacuous must-have <b>alone</b> no longer opens Strong; without a
    /// skill signal it caps at <see cref="Good"/>) — AND at least one stated region/employment
    /// preference is confirmed (none contradicted). "Stark match". On the Fast overload (no
    /// must-have input) this is the top rung — "both secondaries confirmed".
    /// </summary>
    Strong,

    /// <summary>
    /// The golden top rung (Klas 2026-06-20 "Toppmatch"): the ad's must-have requirements are
    /// met (or none — Vacuous), BOTH region and employment are confirmed, AND there is a
    /// positive CV skill / nice-to-have signal (<c>Match</c>/<c>Partial</c>). The strongest
    /// requirement-backed grade. Reachable only via the Full
    /// <see cref="MatchGradeCalculator.Grade(Abstractions.FullMatchScore)"/> overload (the
    /// Fast overload has no skill/must-have input and tops at <see cref="Strong"/>). "Toppmatch".
    /// </summary>
    Top,
}
