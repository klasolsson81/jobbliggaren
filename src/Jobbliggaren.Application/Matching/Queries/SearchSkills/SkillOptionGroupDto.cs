namespace Jobbliggaren.Application.Matching.Queries.SearchSkills;

/// <summary>
/// #277 — one GROUPED skill-taxonomy option: the canonical (preferred-first) concept-id + its
/// display label, plus ALL member concept-ids that share one exact-label surface (the ESCO + AF
/// twins one literal co-produces; a singleton carries exactly one member). Non-PII taxonomy
/// metadata. The wire shape the FE skill chips' "add" search + the saved-chip reverse-lookup
/// render: ONE addable/displayable chip whose label is the canonical, carrying every member id.
/// Selecting/keeping a chip confirms ALL its <see cref="MemberConceptIds"/> on the full-replace
/// save — a read/offer-side projection only, never persisted (PreferredSkills stays FLAT).
/// </summary>
public sealed record SkillOptionGroupDto(
    string CanonicalConceptId, string Label, IReadOnlyList<string> MemberConceptIds);
