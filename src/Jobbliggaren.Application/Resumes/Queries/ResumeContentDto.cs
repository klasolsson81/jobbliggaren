namespace Jobbliggaren.Application.Resumes.Queries;

/// <summary>
/// Transport shape for the canonical CV content. The Fas 4b AppCopy superset fields
/// (<see cref="Languages"/>, <see cref="SkillGroups"/>, <see cref="Sections"/>, ADR 0095)
/// are <b>optional with a null default</b> so a pre-superset client (which omits them)
/// deserialises cleanly; the mapper coalesces null to an empty list. The four original
/// fields keep their established all-required contract.
/// </summary>
public sealed record ResumeContentDto(
    PersonalInfoDto PersonalInfo,
    IReadOnlyList<ExperienceDto> Experiences,
    IReadOnlyList<EducationDto> Educations,
    IReadOnlyList<SkillDto> Skills,
    string? Summary,
    IReadOnlyList<SpokenLanguageDto>? Languages = null,
    IReadOnlyList<SkillGroupDto>? SkillGroups = null,
    IReadOnlyList<ResumeSectionDto>? Sections = null);
