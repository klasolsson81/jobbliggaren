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
    // has stated at least one desired occupation-group. Drives the "du har inte
    // angett ditt drömjobb"-affordance (no stored flag; empty preferences = false).
    bool HasStatedDesiredOccupation)
{
    public static JobSeekerProfileDto FromDomain(JobSeeker js) => new(
        js.Id.Value,
        js.DisplayName,
        js.Preferences.Language,
        js.Preferences.EmailNotifications,
        js.Preferences.WeeklySummary,
        js.CreatedAt,
        js.MatchPreferences.PreferredOccupationGroups.Count > 0);
}
