using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;

public sealed record JobSeekerProfileDto(
    Guid Id,
    string DisplayName,
    string Language,
    bool EmailNotifications,
    bool WeeklySummary,
    DateTimeOffset CreatedAt)
{
    public static JobSeekerProfileDto FromDomain(JobSeeker js) => new(
        js.Id.Value,
        js.DisplayName,
        js.Preferences.Language,
        js.Preferences.EmailNotifications,
        js.Preferences.WeeklySummary,
        js.CreatedAt);
}
