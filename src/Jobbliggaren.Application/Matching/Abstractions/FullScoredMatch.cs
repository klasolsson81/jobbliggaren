namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>
/// #300 PR-4 (ADR 0084 §F4 / §5 point 5; senior-cto-advisor carrier-bind 2026-06-28,
/// Variant A) — the FULL scorer's port result, the frozen <see cref="FullMatchScore"/>
/// PLUS the single bit the grade ladder needs to split exact-vs-related:
/// <see cref="SsykIsRelated"/>.
/// <para>
/// <b>Why a separate carrier, not a field on <see cref="FullMatchScore"/> (PR-2 bind):</b>
/// the score types are arch-pinned BY SHAPE (Goodhart guard — <c>FullMatchScore</c> is
/// exactly { Fast, SkillOverlap, MustHaveCoverage, NiceToHaveCoverage }, see
/// <c>FullMatchScorerLayerTests</c>). The exact-vs-related distinction is therefore carried
/// BESIDE the score, never inside it: PR-2 bound "isRelated = a <c>Grade(...)</c> parameter,
/// not a score field" so the frozen pin stays green. This carrier delivers that parameter
/// from the scorer to the three grade call-sites (the page-tag batch, the modal detail, the
/// background scan) so they can call
/// <see cref="Grading.MatchGradeCalculator.Grade(FullMatchScore, bool)"/> with the right flag.
/// </para>
/// <para>
/// <b><see cref="SsykIsRelated"/> is CATEGORICAL, not a magnitude (Goodhart-safe, ADR 0084
/// §F4):</b> it is a single ladder-branch bit ("did the SSYK gate pass via a RELATED
/// occupation group rather than the user's stated exact group?"), exactly like the
/// <see cref="Grading.MatchGrade.Related"/> rung it produces. It is set TRUE only when the
/// ad's occupation group is in the profile's RELATED ssyk-4 set AND NOT in the exact set
/// (exact-precedence); it cannot be summed into an opaque total or "optimized to 92".
/// </para>
/// <para>
/// <b>Fast vs Full asymmetry (CTO Variant A):</b> only the FULL scorer methods
/// (<see cref="IMatchScorer.ScoreFullAsync"/> / <see cref="IMatchScorer.ScoreFullBatchAsync"/>)
/// carry this — they are the only methods whose result is graded in production. The Fast
/// methods stay <see cref="MatchScore"/>-shaped (no production grade caller); a Fast carrier
/// is a documented future additive wave, created in-block with its first consumer, not now
/// (YAGNI — ADR 0084 §C cadence).
/// </para>
/// Application-layer <c>record class</c> (CLAUDE.md §3.3) — a never-persisted read projection
/// (parity <see cref="FullMatchScore"/>); no EF/Npgsql/NLP type crosses the port surface.
/// </summary>
/// <param name="Score">The embedded FULL match score (the four Fast dimensions + skill /
/// must-have / nice-to-have coverage), unchanged — equal to what the pre-PR-4
/// <c>ScoreFullAsync</c> returned for the same ad and profile.</param>
/// <param name="SsykIsRelated">Whether the SSYK gate passed via the RELATED occupation set
/// only (ad group ∈ related ∧ ∉ exact). Drives the
/// <see cref="Grading.MatchGrade.Related"/> flat cap. <c>false</c> unless the live
/// <c>?includeRelated</c> / <c>?relaterade=on</c> toggle (off by default, #300) populated the
/// profile's related set; with the toggle off that set is empty, so this stays <c>false</c>.</param>
/// <param name="MatchedSkillConceptIds">
/// #477 Low 2 — the concept-ids of the ad's <b>Skill</b> extracted-terms the profile's confirmed
/// skills COVER (the SkillOverlap intersection surfaced as ids, not just Display labels).
/// Deduped + Ordinal-ordered for determinism. This is the persisted explainability EVIDENCE
/// (ADR 0080 / ADR 0071 "explainable by design"): the background scan carries it into
/// <see cref="Domain.Matching.UserJobAdMatch.MatchedSkillConceptIds"/> (before #477 Low 2 the
/// scan wrote an empty list, so the persisted evidence never existed). Carried BESIDE the score
/// (never inside the Goodhart-frozen <see cref="FullMatchScore"/>) exactly like
/// <paramref name="SsykIsRelated"/>: it is a string-list EVIDENCE payload, NOT a magnitude — it
/// cannot be summed into an opaque total (parity the <c>UserJobAdMatchGoodhartTests</c> which
/// bless "the evidence is a string list"). Empty when the profile has no confirmed skills or the
/// ad has no covered Skill terms (honest "nothing to cite").</param>
public sealed record FullScoredMatch(
    FullMatchScore Score,
    bool SsykIsRelated,
    IReadOnlyList<string> MatchedSkillConceptIds);
