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
    IReadOnlyList<string> PreferredMunicipalities)
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
        js.MatchPreferences.PreferredMunicipalities);
}
