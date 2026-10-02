using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;

public sealed record JobSeekerProfileDto(
    Guid Id,
    string Language,
    // ADR 0080 Vag 4 PR-6 — background-match notification consent (opt-in, GDPR Art. 6/7,
    // default OFF per PR-1) + the digest cadence, projected here so the settings toggle +
    // cadence picker pre-fill the user's current state. The consent TIMESTAMPS (Art. 7
    // evidence) are deliberately NOT projected; the UI needs only the enabled flag + cadence
    // (data-minimal). (TD-115: the legacy EmailNotifications/WeeklySummary projections were
    // retired alongside the flags — they gated no email path.)
    bool BackgroundMatchNotificationsEnabled,
    DigestCadence DigestCadence,
    // ADR 0087 D3/D5 (#311 PR-2b) — the SEPARATE company-follow notification consent flag
    // (opt-in, GDPR Art. 6/7, default OFF per PR-4), projected so the settings follow-consent
    // toggle pre-fills the user's current state. A distinct processing purpose from
    // BackgroundMatchNotificationsEnabled (ADR 0087 D5), toggled via
    // PUT /me/followed-company-notification-consent. Data-minimal: the Art. 7 consent TIMESTAMPS
    // are deliberately NOT projected (parity BackgroundMatchNotificationsEnabled) — the UI needs
    // only the enabled flag. The digest cadence is shared (DigestCadence above, ADR 0087 D2).
    bool FollowedCompanyNotificationsEnabled,
    DateTimeOffset CreatedAt,
    // F4-12 (ADR 0076) — derived signal for the setup nudge: true once the user
    // has stated at least one desired occupation-group. Drives the "ange vilka
    // yrken du söker inom"-affordance (no stored flag; empty preferences = false).
    bool HasStatedDesiredOccupation,
    IReadOnlyList<string> PreferredOccupationGroups,
    IReadOnlyList<string> PreferredRegions,
    IReadOnlyList<string> PreferredEmploymentTypes,
    IReadOnlyList<string> PreferredMunicipalities,
    bool PreferredRemote,
    IReadOnlyList<string> PreferredSkills,
    int? ExperienceYears,
    IReadOnlyList<OccupationExperienceDto> PreferredOccupationExperience)
{
    public static JobSeekerProfileDto FromDomain(JobSeeker js) => new(
        js.Id.Value,
        js.Preferences.Language,
        js.Preferences.BackgroundMatchNotificationsEnabled,
        js.Preferences.DigestCadence,
        js.Preferences.FollowedCompanyNotificationsEnabled,
        js.CreatedAt,
        js.MatchPreferences.PreferredOccupationGroups.Count > 0,
        js.MatchPreferences.PreferredOccupationGroups,
        js.MatchPreferences.PreferredRegions,
        js.MatchPreferences.PreferredEmploymentTypes,
        js.MatchPreferences.PreferredMunicipalities,
        js.MatchPreferences.PreferredRemote,
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
