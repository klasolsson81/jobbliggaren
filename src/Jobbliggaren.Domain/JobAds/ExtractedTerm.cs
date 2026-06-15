namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// One deterministically-extracted term from a job ad (F4-4, ADR 0071/0074 —
/// no AI/LLM). Consumed by the matching engine (F4-6) to compute keyword/skill
/// overlap + requirement coverage against a CV. Every term cites its evidence
/// (<see cref="MatchedOn"/>) — explainable by design (CLAUDE.md §5; ADR 0074
/// evidence-citation invariant): an extracted term is never an opaque token.
/// <para>
/// This is persisted aggregate state (jsonb on <c>job_ads</c>), so it lives in
/// Domain — unlike F4-3's <c>OccupationCandidate</c>, which is a never-persisted
/// read projection in Application. Same layering rule ("the concept lives where
/// its lifecycle lives"), different answer for a different lifecycle
/// (dotnet-architect, ADR 0075).
/// </para>
/// </summary>
/// <param name="Lexeme">The canonical overlap/match token used for set overlap
/// against a CV. For a <see cref="ExtractedTermKind.Keyword"/> this is the
/// Snowball stem (<c>to_tsvector('swedish')</c> parity, F4-2); for a
/// <see cref="ExtractedTermKind.Skill"/> this is the JobTech taxonomy
/// <b>concept-id</b> (concept-level overlap). Never empty. This field backs the
/// STORED generated <c>extracted_lexemes text[]</c> GIN column.</param>
/// <param name="Display">Human-readable form: the skill's preferred label, or a
/// representative surface form of the keyword. Never empty (UI + evidence).</param>
/// <param name="Kind">Whether the term is a taxonomy-resolved skill, a raw
/// keyword, or an employer requirement (the last reserved for F4-4b).</param>
/// <param name="Source">Where in the ad the term was found (title/description;
/// must-have/nice-to-have reserved for F4-4b).</param>
/// <param name="MatchedOn">The cited evidence: the matched skill label/synonym
/// span, or the keyword's surface word. Never empty (evidence-citation
/// invariant).</param>
/// <param name="ConceptId">The JobTech taxonomy concept-id for a
/// <see cref="ExtractedTermKind.Skill"/> (equals <paramref name="Lexeme"/>);
/// <c>null</c> for a <see cref="ExtractedTermKind.Keyword"/>.</param>
/// <param name="Weight">Deterministic relevance (specificity × frequency); higher
/// = more representative. Must be finite and non-negative.</param>
public sealed record ExtractedTerm(
    string Lexeme,
    string Display,
    ExtractedTermKind Kind,
    ExtractedTermSource Source,
    string MatchedOn,
    string? ConceptId,
    double Weight);

/// <summary>
/// What an <see cref="ExtractedTerm"/> represents. The bounded, deterministic term
/// list is sorted primarily by a Kind→rank function (<see cref="ExtractedTerms"/>'s
/// <c>SortRank</c>, NOT the raw declaration order below):
/// <see cref="Requirement"/> → <see cref="Skill"/> → <see cref="Keyword"/> (F4-4b,
/// CTO Decision 1c — an employer-stated requirement is the highest-authority match
/// signal and must survive the cap before NLP-derived skills/keywords). The numeric
/// declaration order is kept stable (Skill=0, Keyword=1, Requirement=2) so the
/// persisted jsonb enum strings of existing rows never shift; the sort rank is
/// applied independently.
/// </summary>
public enum ExtractedTermKind
{
    /// <summary>A term resolved to a JobTech skill/competence taxonomy concept
    /// (carries a <see cref="ExtractedTerm.ConceptId"/>). Highest-value signal.</summary>
    Skill,

    /// <summary>A salient free-text term not resolved to the skill taxonomy
    /// (no concept-id). The honest fallback the OQ4 coverage gap implies.</summary>
    Keyword,

    /// <summary>An employer-stated requirement (must-have/nice-to-have). Reserved
    /// for F4-4b — never populated this STEG (the structured requirement data is
    /// not yet ingested; ADR 0074 Path C).</summary>
    Requirement,
}

/// <summary>
/// Where in the job ad an <see cref="ExtractedTerm"/> originated (cited evidence
/// provenance). <see cref="MustHave"/>/<see cref="NiceToHave"/> are reserved for
/// F4-4b (employer requirement extraction) and are never produced this STEG.
/// </summary>
public enum ExtractedTermSource
{
    /// <summary>The ad title (headline).</summary>
    Title,

    /// <summary>The ad free-text description.</summary>
    Description,

    /// <summary>An employer must-have requirement (reserved, F4-4b).</summary>
    MustHave,

    /// <summary>An employer nice-to-have requirement (reserved, F4-4b).</summary>
    NiceToHave,
}
