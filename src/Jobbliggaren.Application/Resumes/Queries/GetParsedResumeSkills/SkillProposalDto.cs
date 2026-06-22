namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeSkills;

/// <summary>
/// A CV-resolved JobTech skill proposal surfaced to the FE (ADR 0079 STEG 3): a
/// taxonomy concept-id + its preferred (canonical) label for the skill chip. Non-PII
/// (taxonomy metadata only). The wire projection of the Domain <c>ProposedSkill</c> —
/// the FE pre-adds these as removable chips (propose-and-approve), the user confirms by
/// saving the full-replace MatchPreferences.PreferredSkills set.
/// </summary>
public sealed record SkillProposalDto(string ConceptId, string Label);
