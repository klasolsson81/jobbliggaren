using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Matching.Grading;

/// <summary>
/// The page-scoped match TAG grade for the /jobb list card (F4-13, ADR 0076 +
/// 2026-06-19 graded-ladder re-bind; senior-cto-advisor RB1/RB2). A <b>named ordinal
/// CATEGORY</b>, not a number — the visual twin of the F4-3 <c>OccupationMatchKind</c>
/// precedent ADR 0076 Decision 4 itself cites. It is NOT the forbidden opaque total:
/// it has exactly three members, cannot be "optimized to 92", and the user always sees
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
/// <b>F4-13 bottom rungs, F4-16 paints the golden top rung:</b> Basic/Good/Strong are the
/// bottom of Klas's full ladder, computed from the Fast dimensions (occupation + region +
/// employment). F4-15 added the skill signal to the internal sort-key only (b-ii); F4-16
/// makes it VISIBLE by extending the SAME enum UPWARD with <see cref="Top"/> ("Toppmatch")
/// ABOVE <see cref="Strong"/> — a true "golden match" earned when a Strong Fast grade ALSO
/// has CV-skill overlap with the ad. The extension is rework-free (ADR 0076 Amendment (b)
/// §1): the lower three rungs are never rewritten, and skill never demotes an honest Strong
/// (positive-only ladder). The golden-rung derivation lives in
/// <see cref="MatchGradeCalculator.Grade(Abstractions.FullMatchScore)"/>.
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
    /// Occupation matches AND exactly one of the stated region/employment preferences is
    /// confirmed (with no contradicted preference). "Bra match".
    /// </summary>
    Good,

    /// <summary>
    /// Occupation matches AND both stated region and employment preferences are confirmed
    /// (no contradiction). The strongest grade the Fast tier can award. "Stark match".
    /// </summary>
    Strong,

    /// <summary>
    /// The golden top rung (F4-16, ADR 0076 Amendment (b) §1; Klas 2026-06-20 "Toppmatch"):
    /// a <see cref="Strong"/> Fast match whose CV skills ALSO overlap the ad
    /// (<c>FullMatchScore.SkillOverlap</c> is <c>Match</c>/<c>Partial</c>). Awarded ONLY on
    /// top of <see cref="Strong"/> — skill never promotes a sub-Strong grade and never
    /// demotes (positive-only ladder, rework-free). "Toppmatch".
    /// </summary>
    Top,
}
