namespace Jobbliggaren.Domain.Resumes;

public sealed record Experience(
    string Company,
    string Role,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Description);
