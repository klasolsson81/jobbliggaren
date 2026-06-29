namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeSkills;

/// <summary>
/// A CV-resolved JobTech skill proposal surfaced to the FE (ADR 0079 STEG 3): a taxonomy
/// concept-id + its preferred (canonical) label for the skill chip. Non-PII (taxonomy metadata
/// only). A READ-projection of the persisted-flat Domain <c>ProposedSkill</c> — the FE pre-adds
/// these as removable chips (propose-and-approve), the user confirms by saving the full-replace
/// MatchPreferences.PreferredSkills set.
/// <para>
/// #277 — GROUPED by shared exact-label surface at this READ surface (the persisted
/// <c>ProposedSkill</c> stays FLAT — grouping is a read-projection concern, never applied in
/// ImportResumeCommandHandler): <see cref="ConceptId"/> is the canonical (preferred-first) id and
/// <see cref="MemberConceptIds"/> carries ALL member ids the surface co-produces (a singleton
/// carries one), so an ESCO + AF twin-pair proposal renders as ONE chip. Keeping the chip
/// confirms all member ids on the full-replace save.
/// </para>
/// </summary>
public sealed record SkillProposalDto(
    string ConceptId, string Label, IReadOnlyList<string> MemberConceptIds);
