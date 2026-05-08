namespace JobbPilot.Application.Resumes.Queries;

public sealed record ResumeContentDto(
    PersonalInfoDto PersonalInfo,
    IReadOnlyList<ExperienceDto> Experiences,
    IReadOnlyList<EducationDto> Educations,
    IReadOnlyList<SkillDto> Skills,
    string? Summary);
