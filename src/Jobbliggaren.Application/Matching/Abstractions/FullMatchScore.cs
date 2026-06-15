namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>
/// The deterministic FULL match score (F4-6, ADR 0074 row U5b; senior-cto-advisor
/// Decision A = A2) — built ON TOP OF the F4-5 Fast-mode score. It embeds the
/// frozen <see cref="MatchScore"/> (the four thin-vertical dimensions) and adds
/// three new dimensions computed from the ad's deterministically-extracted terms
/// (F4-4/F4-4b) against the CV's skill concept-ids: skill overlap + must-have /
/// nice-to-have requirement coverage. NO AI/LLM (ADR 0071, CLAUDE.md §5).
/// <para>
/// <b>Composition, not a flat 6-dim type (A2, CTO Decision A):</b> embedding
/// <see cref="MatchScore"/> honours "F4-6 builds on top of F4-5" literally (OCP —
/// open for extension via a new type, closed for modification of the frozen one).
/// A flat type would re-declare the four Fast dimensions and duplicate the
/// knowledge "what is the Fast shape" (DRY at the knowledge level). The F4-5
/// 4-property pin on <see cref="MatchScore"/> stays GREEN; a parallel arch-test
/// pins THIS shape so the Goodhart guard extends to F4-6.
/// </para>
/// <para>
/// <b>Category-primary, NO opaque total (Goodhart guard — CLAUDE.md §5, ADR 0071,
/// ADR 0074):</b> there is intentionally NO aggregate <c>Value: 0-100</c> and NO
/// numeric weight blending must-have over nice-to-have. The per-dimension verdicts
/// + matched/missing evidence ARE the score. must-have is the binding requirement
/// signal (F4-4b Decision 1A), but it is just its own dimension's verdict — there
/// is no combined number it could gate.
/// </para>
/// <para>
/// Application-layer <c>record class</c> (CLAUDE.md §3.3) — a never-persisted read
/// projection computed on demand (parity <see cref="MatchScore"/>); no
/// EF/Npgsql/NLP type crosses the port surface. The full result nests the Fast
/// result (<c>full.Fast.SsykOverlap</c>) — the nesting mirrors the real
/// "Full = Fast + 3" relationship honestly; a transport DTO may flatten it at the
/// query boundary (CLAUDE.md §2.3) without the result type lying about its shape.
/// </para>
/// </summary>
/// <param name="Fast">The embedded F4-5 Fast-mode score (SSYK overlap, title
/// similarity, region fit, employment fit), unchanged — equal to what
/// <c>ScoreAsync</c> would return for the same ad and embedded Fast profile.</param>
/// <param name="SkillOverlap">The ad's extracted Skill terms vs the CV's skill
/// concept-ids (concept-id overlap, surfaced as Display labels). Skill-only v1 —
/// keyword overlap is omitted, not a <c>NotAssessed</c> placeholder
/// (CTO Decision E).</param>
/// <param name="MustHaveCoverage">The ad's <c>must_have</c> Requirement skill
/// terms covered by the CV's skills (the binding requirement signal,
/// F4-4b Decision 1A; CTO Decision D).</param>
/// <param name="NiceToHaveCoverage">The ad's <c>nice_to_have</c> Requirement skill
/// terms covered by the CV's skills (the bonus bucket; same set-emptiness verdict
/// logic, never penalises on absence — CTO Decision D).</param>
public sealed record FullMatchScore(
    MatchScore Fast,
    MatchDimension SkillOverlap,
    MatchDimension MustHaveCoverage,
    MatchDimension NiceToHaveCoverage);
