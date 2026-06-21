using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>
/// How a single match dimension scored (F4-5, ADR 0074 row U5a; senior-cto-advisor
/// Decision 3 = V3-a). An explainability/honesty contract — not a domain invariant —
/// so it lives beside the port, mirroring <c>OccupationMatchKind</c> / <c>TextLanguage</c>.
/// <list type="bullet">
/// <item><see cref="Match"/> — data present on both sides and they overlap (for
/// title: every ad lexeme is covered).</item>
/// <item><see cref="Partial"/> — partial overlap with leftover (title dimension
/// only; the set-membership dimensions are binary and never report Partial).</item>
/// <item><see cref="NoMatch"/> — data present on <b>both</b> sides and disjoint.</item>
/// <item><see cref="NotAssessed"/> — the <b>CV side</b> for the dimension is empty
/// (no CV / no resolved skills), so we cannot assess it. The honest "not assessed v1"
/// state (CLAUDE.md §5) — never conflated with <see cref="NoMatch"/>.</item>
/// <item><see cref="Vacuous"/> — the <b>ad side</b> for a concept-coverage dimension
/// is empty WHILE the CV side is non-empty: we looked, and the ad specifies none of
/// this kind (e.g. an ad with no <c>must_have</c> requirements at all). Distinct from
/// <see cref="NotAssessed"/> (which is "we could not assess") — Vacuous is "there was
/// nothing to require". This distinction is load-bearing for the requirement-aware
/// grade (ADR 0076 amendment 2026-06-20, Klas Reading 1): a no-must-have ad is
/// gate-OPEN (a qualified candidate CAN reach Stark/Topp), whereas a no-CV user
/// (<see cref="NotAssessed"/> must-have) is gate-CLOSED (caps at the preference rung).
/// Only the set-membership concept-coverage dimensions (skill / must-have /
/// nice-to-have) can be Vacuous; the binary Fast dimensions never are.</item>
/// </list>
/// <para>
/// Serialized by NAME, not ordinal (<c>[JsonStringEnumConverter]</c>) — F4-13's match
/// batch DTO is the first surface to put this enum on the wire; named verdicts are
/// self-documenting and reorder-safe (parity <see cref="Grading.MatchGrade"/>).
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatchDimensionVerdict
{
    Match,
    Partial,
    NoMatch,
    NotAssessed,

    /// <summary>
    /// The ad side of a concept-coverage dimension is empty while the CV side is
    /// non-empty ("nothing required, and we looked") — see the type summary. Added
    /// 2026-06-20 (ADR 0076 amendment) for the requirement-aware grade's vacuous-ad
    /// gate-open case; never produced by the binary Fast membership dimensions.
    /// </summary>
    Vacuous,
}

/// <summary>
/// One scored dimension of the Fast-mode match, with cited evidence
/// (explainable-by-design — CLAUDE.md §5 / ADR 0071: a match is never an opaque
/// number). <see cref="Matched"/> is the overlapping evidence (matching concept ids
/// / shared title lexemes); <see cref="Missing"/> is what the ad wants that the CV
/// lacks (the civic-useful direction: "what you are missing for this ad"). There is
/// deliberately NO numeric score on a dimension — a per-dimension number would
/// invite summing into the opaque total the Goodhart guard forbids (CTO Decision 0).
/// </summary>
public sealed record MatchDimension(
    MatchDimensionVerdict Verdict,
    IReadOnlyList<string> Matched,
    IReadOnlyList<string> Missing);

/// <summary>
/// The deterministic "Fast mode" match score (F4-5, BUILD §8.2, ADR 0074 row U5a).
/// A thin vertical over exactly four dimensions — SSYK level-4 overlap, stemmed
/// title similarity, location ("ort") fit, employment-type fit. <see cref="RegionFit"/>
/// is the location dimension: a region ∪ municipality union (Spår 3, ADR 0076-amendment
/// 2026-06-21) — the property keeps the name <c>RegionFit</c> while its semantics widened
/// to two granularities. It consumes NEITHER F4-4/F4-4b
/// keyword/skill nor requirement extraction (those are F4-6's <c>skillMatch</c> /
/// <c>requirementCoverage</c> dimensions, in a future full-match type) — they are
/// deliberately absent here, not <c>NotAssessed</c> placeholders (CTO Decision 0).
/// <para>
/// <b>Category-primary, no opaque total (Goodhart guard — CLAUDE.md §5, ADR 0071,
/// ADR 0074):</b> there is intentionally NO aggregate <c>Value: 0-100</c>. The
/// per-dimension verdicts + matched/missing evidence ARE the score; the user sees
/// exactly why each dimension landed where it did. (BUILD.local.md §5.2/§5.6's older
/// pre-no-AI <c>MatchScore.Value</c> catalogue is a documented spec-edit pending Klas
/// — CTO Decision 0; this is the newer §8.2/ADR-0074 contract.)
/// </para>
/// Application-layer <c>record class</c> (CLAUDE.md §3.3) — a never-persisted read
/// projection (parity <see cref="OccupationDerivationResult"/>), computed on demand;
/// no EF/Npgsql/NLP type crosses the port surface.
/// </summary>
public sealed record MatchScore(
    MatchDimension SsykOverlap,
    MatchDimension TitleSimilarity,
    MatchDimension RegionFit,
    MatchDimension EmploymentFit);
