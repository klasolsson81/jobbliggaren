namespace Jobbliggaren.Application.Matching.Queries.SearchSkills;

/// <summary>
/// One skill-taxonomy typeahead option (ADR 0079 STEG 3 PR-C): a concept-id + its
/// canonical preferred label. Non-PII taxonomy metadata. The wire shape the FE skill
/// chips' "add" search renders; selecting one adds the concept-id to the draft
/// PreferredSkills set (confirmed on the full-replace save).
/// </summary>
public sealed record SkillOptionDto(string ConceptId, string Label);
