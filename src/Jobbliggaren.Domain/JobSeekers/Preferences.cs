namespace Jobbliggaren.Domain.JobSeekers;

public sealed record Preferences(
    string Language = "sv",
    bool EmailNotifications = true,
    bool WeeklySummary = false);
