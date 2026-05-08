namespace JobbPilot.Domain.Resumes;

public sealed record Education(
    string Institution,
    string Degree,
    DateOnly StartDate,
    DateOnly? EndDate);
