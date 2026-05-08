namespace JobbPilot.Domain.Resumes;

public sealed record PersonalInfo(
    string FullName,
    string? Email,
    string? Phone,
    string? Location);
