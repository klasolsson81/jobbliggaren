namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// An unconfirmed JobTech skill-taxonomy proposal carried on a parsed CV (ADR 0079
/// STEG 3, ADR 0040 Beslut 4: the engine PROPOSES, the user CONFIRMS). A Domain mirror
/// of the resolved skill (a taxonomy concept-id + its preferred label) — the aggregate
/// owns its own state and never references an Application type. Non-PII (a taxonomy id +
/// a canonical label): persisted as plain jsonb, symmetric with <see cref="ProposedOccupation"/>.
/// Confirmation (writing the concept-id into the user's MatchPreferences.PreferredSkills)
/// happens later via the FE full-replace save — never auto-confirmed here.
/// </summary>
public sealed record ProposedSkill(
    string ConceptId,
    string Label);
