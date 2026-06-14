namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Result of an SSYK level-4 derivation (F4-3). <see cref="Candidates"/> is a
/// deterministically-ordered, deduplicated ranked list of proposed ssyk-4
/// occupation-groups for the user to confirm (ADR 0040 Beslut 4). An empty list
/// means no taxonomy match → manual SSYK selection. The list is <b>bounded</b> to
/// the most-relevant candidates (a high-overlap title is capped): this is a
/// relevance/UX bound, not a coverage claim — absent a precise match the UX falls
/// to manual selection regardless, and exact hits (few) always survive. <see
/// cref="Title"/> echoes the input verbatim. Application-layer <c>record class</c>
/// (CLAUDE.md §3.3) — no EF/Npgsql/NLP type crosses the port surface.
/// </summary>
public sealed record OccupationDerivationResult(
    string Title,
    IReadOnlyList<OccupationCandidate> Candidates);

/// <summary>
/// One proposed ssyk-level-4 occupation-group, with cited evidence
/// (explainable-by-design, CLAUDE.md §5 — a derivation is never an opaque pick).
/// <see cref="OccupationGroupConceptId"/> is the ssyk-4 id that drops straight into
/// <c>SearchCriteria.OccupationGroup</c>; <see cref="MatchedOn"/> is the
/// occupation-name label span that grounded the match.
/// </summary>
public sealed record OccupationCandidate(
    string OccupationGroupConceptId,
    string OccupationGroupLabel,
    OccupationMatchKind MatchKind,
    string MatchedOn);

/// <summary>
/// How a candidate was derived (explainability/audit). Pure V2 (senior-cto-advisor
/// Decision 1): the free-text title is matched against occupation-<b>name</b>
/// labels and resolved up to ssyk-4 — there is no group-label match kind (the
/// rejected V1/V3 branch). Lives in Application beside the port, mirroring
/// <c>SuggestionKind</c> / <c>TextLanguage</c>. Declaration order is load-bearing:
/// <see cref="ExactOccupationName"/> (0) sorts before
/// <see cref="StemmedTokenOverlap"/> (1) in the ranked result.
/// </summary>
public enum OccupationMatchKind
{
    /// <summary>The title equals an occupation-name label (OrdinalIgnoreCase, no
    /// diacritic folding — Decision 4). Highest-precision evidence.</summary>
    ExactOccupationName,

    /// <summary>The title's Snowball lexemes overlap an occupation-name label's
    /// lexemes (local NLP tier, F4-2 — to_tsvector('swedish') parity). Lower
    /// precision than an exact hit → ranked after all exact candidates.</summary>
    StemmedTokenOverlap,
}
