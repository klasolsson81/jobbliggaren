using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;

public sealed record JobSeekerProfileDto(
    Guid Id,
    string DisplayName,
    string Language,
    bool EmailNotifications,
    bool WeeklySummary,
    DateTimeOffset CreatedAt,
    // F4-12 (ADR 0076) — derived signal for the setup nudge: true once the user
    // has stated at least one desired occupation-group. Drives the "ange vilka
    // yrken du söker inom"-affordance (no stored flag; empty preferences = false).
    bool HasStatedDesiredOccupation,
    // F4-12 (ADR 0076) — the stated match preferences, projected so the settings
    // card pre-fills the user's current selections. Required because the write is
    // a full-replace PUT: without the current lists, editing would silently wipe
    // prior selections. Concept-id projections of the VO (no domain leak, no PII).
    // PreferredMunicipalities is the Spår 3 read-side partner (ADR 0076-amendment
    // 2026-06-21, PR-D): the län→kommun cascade's full-replace PUT MUST round-trip
    // municipalities through this projection, or saving region preferences would wipe
    // the user's stated municipalities (the one wipe-risk in the arc — landed here
    // atomically with the FE send).
    IReadOnlyList<string> PreferredOccupationGroups,
    IReadOnlyList<string> PreferredRegions,
    IReadOnlyList<string> PreferredEmploymentTypes,
    IReadOnlyList<string> PreferredMunicipalities,
    // ADR 0079 STEG 3 — the confirmed skill concept-ids + stated years of experience,
    // projected so the settings/wizard skill section pre-fills the user's current set.
    // Required for the same full-replace-PUT page-wipe reason as the lists above: without
    // round-tripping skills + experience, saving any other dimension would silently wipe
    // them. Concept-id + scalar projections of the VO (no domain leak, no PII).
    IReadOnlyList<string> PreferredSkills,
    int? ExperienceYears,
    // ADR 0079-amendment (exp-per-occ PR-3) — the per-occupation experience overlay,
    // projected so the wizard's per-occupation year inputs pre-fill. SAME full-replace
    // page-wipe reason: without round-tripping it, saving any other dimension would wipe
    // the overlay. A {conceptId, years} projection of the VO (no domain leak, no PII).
    IReadOnlyList<OccupationExperienceDto> PreferredOccupationExperience)
{
    public static JobSeekerProfileDto FromDomain(JobSeeker js) => new(
        js.Id.Value,
        js.DisplayName,
        js.Preferences.Language,
        js.Preferences.EmailNotifications,
        js.Preferences.WeeklySummary,
        js.CreatedAt,
        js.MatchPreferences.PreferredOccupationGroups.Count > 0,
        js.MatchPreferences.PreferredOccupationGroups,
        js.MatchPreferences.PreferredRegions,
        js.MatchPreferences.PreferredEmploymentTypes,
        js.MatchPreferences.PreferredMunicipalities,
        js.MatchPreferences.PreferredSkills,
        js.MatchPreferences.ExperienceYears,
        [.. js.MatchPreferences.PreferredOccupationExperience
            .Select(e => new OccupationExperienceDto(e.ConceptId, e.Years))]);
}

/// <summary>
/// Read projection of one per-occupation experience overlay entry (ADR 0079-amendment) —
/// the {conceptId, years} the FE reads to pre-fill the wizard's year input. Non-PII.
/// </summary>
public sealed record OccupationExperienceDto(string ConceptId, int? Years);
