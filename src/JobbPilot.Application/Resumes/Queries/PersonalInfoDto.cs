namespace JobbPilot.Application.Resumes.Queries;

public sealed record PersonalInfoDto(
    string FullName,
    string? Email,
    string? Phone,
    string? Location);
