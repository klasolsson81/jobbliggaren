namespace JobbPilot.Application.Resumes.Queries;

public sealed record EducationDto(
    string Institution,
    string Degree,
    DateOnly StartDate,
    DateOnly? EndDate);
