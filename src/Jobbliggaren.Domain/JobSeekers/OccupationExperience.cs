namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// A per-occupation experience annotation (ADR 0079-amendment 2026-06-23) — the user's
/// stated ~years of experience in one preferred occupation group (JobTech SSYK-4
/// concept-id). Supersedes the single profile-level <see cref="MatchPreferences.ExperienceYears"/>
/// scalar (ADR 0079 Beslut 1(e)).
///
/// <para>A <b>sparse overlay</b> on <see cref="MatchPreferences.PreferredOccupationGroups"/>:
/// an entry MAY exist only for a concept-id that is also a preferred occupation group
/// (the subset invariant, enforced in <see cref="MatchPreferences.Create"/>), and not every
/// group needs one. <see cref="Years"/> is nullable: <c>null</c> = "not stated" (honest — e.g.
/// a career-changer who prefers a group with zero history in it); <c>0..MaxExperienceYears</c>
/// is the believed human range.</para>
///
/// <para>STORED + SURFACED only, never scored (ADR 0079 Beslut 7 / TD-B; a "years ≥ required"
/// threshold would break Goodhart / CLAUDE.md §5). <see cref="Years"/> (never <c>*Score/*Value/
/// *Rank</c>) is a preference INPUT, never a match-result magnitude (Goodhart guard).
/// <see cref="ConceptId"/> / <see cref="Years"/> are the jsonb-key contract (see
/// <c>MatchPreferencesConverters</c>).</para>
/// </summary>
public sealed record OccupationExperience(string ConceptId, int? Years);
