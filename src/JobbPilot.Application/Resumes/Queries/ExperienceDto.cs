namespace JobbPilot.Application.Resumes.Queries;

public sealed record ExperienceDto(
    string Company,
    string Role,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description);
