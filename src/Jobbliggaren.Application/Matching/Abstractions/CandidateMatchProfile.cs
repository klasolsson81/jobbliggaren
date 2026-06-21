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
    IReadOnlyList<string> PreferredMunicipalityConceptIds);
