namespace Jobbliggaren.Application.Matching.Abstractions;

/// <summary>
/// Fas 4 STEG 5 (F4-5, ADR 0074 row U5a; senior-cto-advisor Decision 1+2 = V1-b)
/// — the CV-side input to the deterministic "Fast mode" match score. A plain,
/// caller-supplied value object (BCL-only, parity <see cref="OccupationDerivationResult"/>):
/// it carries the candidate's job title (for the stemmed title-similarity
/// dimension) plus the <b>already F4-3-confirmed</b> SSYK level-4 group ids and the
/// preferred region/employment-type/municipality concept ids.
/// <para>
/// <b><see cref="PreferredMunicipalityConceptIds"/> (Spår 3, ADR 0076-amendment
/// 2026-06-21):</b> the finer-grained location granularity that folds into the same
/// location ("ort") dimension as <see cref="PreferredRegionConceptIds"/>. Carried here
/// additively from PR-A; <c>MatchScorer</c> consumes it (region∪municipality union)
/// from PR-B onward. An empty list = honest "no municipality stated".
/// </para>
/// <para>
/// <b>Why pre-confirmed, not derived here (ADR 0040 Beslut 4):</b> the SSYK
/// derivation (<see cref="IOccupationCodeDeriver"/>, F4-3) <i>proposes</i> a ranked
/// candidate list and the user <i>confirms</i> — it never auto-selects. The scorer
/// therefore consumes the ids the user actually confirmed (carried here); it does
/// NOT silently re-derive and pick one. A caller without confirmed ids passes an
/// empty list → that dimension reports <c>NotAssessed</c> (honest: no SSYK signal),
/// never <c>NoMatch</c>.
/// </para>
/// <para>
/// <b>PII boundary (ADR 0074 Invariant 3):</b> <see cref="Title"/> is a plain
/// string the caller supplies already structured — the same boundary as
/// <see cref="IOccupationCodeDeriver"/> (security-auditor cleared). The
/// personnummer guard + field-encryption pipeline live at the F4-8 call-site that
/// produces this profile from CV content; F4-5 reads no raw CV PII and never logs.
/// </para>
/// </summary>
public sealed record CandidateMatchProfile(
    string Title,
    IReadOnlyList<string> SsykGroupConceptIds,
    IReadOnlyList<string> PreferredRegionConceptIds,
    IReadOnlyList<string> PreferredEmploymentTypeConceptIds,
    IReadOnlyList<string> PreferredMunicipalityConceptIds)
{
    /// <summary>
    /// <b><see cref="RelatedSsykGroupConceptIds"/> (ADR 0084 §F2/§5, issue #300):</b> the
    /// RELATED (substitutable) ssyk-4 group concept-ids — the neighbouring occupation groups
    /// derived from the user's confirmed occupations via JobTech's <c>substitutability</c>
    /// relation (PR-1's <c>taxonomy_relations</c> snapshot behind <c>ITaxonomyReadModel</c>).
    /// The scorer's SSYK gate broadens from "ad group ∈ exact" to "ad group ∈ (exact ∪
    /// related)"; the calculator caps a related-only hit at <see cref="Grading.MatchGrade.Related"/>.
    /// <para>
    /// <b>Additive init-property with an empty default (NOT a positional parameter):</b> an
    /// empty list = "no related set supplied" = today's exact-only behaviour, so every existing
    /// 5-argument construction is unchanged and PR-2 is behavior-inert. The profile builder that
    /// POPULATES this from the taxonomy read-model is wired in PR-3 (ADR 0084 §Implementation —
    /// the SPOT injection in <c>MatchProfileBuilder.FastFromPreferences</c>); until then the set
    /// is always empty in production. <see cref="FullCandidateMatchProfile"/> reads it via its
    /// embedded <see cref="FullCandidateMatchProfile.Fast"/> profile (mirrored, no own field —
    /// the Full profile is arch-pinned to exactly { Fast, CvSkillConceptIds }).
    /// </para>
    /// </summary>
    public IReadOnlyList<string> RelatedSsykGroupConceptIds { get; init; } = [];
}
