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
/// <item><see cref="NotAssessed"/> — the CV-side input for the dimension is empty,
/// or the ad's value is absent (NULL shadow column / no lexemes). The honest
/// "not assessed v1" state (CLAUDE.md §5) — never conflated with <see cref="NoMatch"/>.</item>
/// </list>
/// </summary>
public enum MatchDimensionVerdict
{
    Match,
    Partial,
    NoMatch,
    NotAssessed,
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
/// title similarity, region fit, employment-type fit. It consumes NEITHER F4-4/F4-4b
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
