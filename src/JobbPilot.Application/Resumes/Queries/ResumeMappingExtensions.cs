using JobbPilot.Domain.Resumes;

namespace JobbPilot.Application.Resumes.Queries;

internal static class ResumeMappingExtensions
{
    public static ResumeContentDto ToDto(this ResumeContent c) => new(
        new PersonalInfoDto(
            c.PersonalInfo.FullName,
            c.PersonalInfo.Email,
            c.PersonalInfo.Phone,
            c.PersonalInfo.Location),
        c.Experiences
            .Select(e => new ExperienceDto(e.Company, e.Role, e.StartDate, e.EndDate, e.Description))
            .ToList(),
        c.Educations
            .Select(e => new EducationDto(e.Institution, e.Degree, e.StartDate, e.EndDate))
            .ToList(),
        c.Skills
            .Select(s => new SkillDto(s.Name, s.YearsExperience))
            .ToList(),
        c.Summary);

    public static ResumeVersionDto ToDto(this ResumeVersion v) => new(
        v.Id.Value,
        v.Kind.Name,
        v.Content.ToDto(),
        v.CreatedAt,
        v.UpdatedAt);

    public static ResumeDetailDto ToDetailDto(this Resume r) => new(
        r.Id.Value,
        r.Name,
        r.CreatedAt,
        r.UpdatedAt,
        r.Versions
            .Where(v => v.DeletedAt is null)
            .Select(v => v.ToDto())
            .ToList());
}
