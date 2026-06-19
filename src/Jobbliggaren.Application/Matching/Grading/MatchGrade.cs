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
/// <b>F4-13 bottom rungs, F4-15 extends UPWARD:</b> these three grades are the bottom
/// of Klas's full ladder, computed from the Fast dimensions available now (occupation +
/// region + employment). F4-15 (skill-resolver + Full tier) extends the SAME enum/rule
/// with skill / must-have / nice-to-have rungs ABOVE <see cref="Strong"/> (a true
/// "golden match"), never rewriting these three.
/// </para>
/// <para>
/// <b>Serialized by NAME, not ordinal (F4-13 is the first surface to put this on the
/// wire):</b> the JSON value is <c>"Strong"</c>/<c>"Good"</c>/<c>"Basic"</c>, never a
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
    /// (no contradiction). The strongest grade F4-13's Fast tier can award (F4-15 adds
    /// higher skill/requirement rungs). "Stark match".
    /// </summary>
    Strong,
}
