using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Queries;

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
        c.Summary,
        // Fas 4b AppCopy superset (ADR 0094). Proficiency projects the SmartEnum Name token.
        c.Languages
            .Select(l => new SpokenLanguageDto(l.Name, l.Proficiency.Name))
            .ToList(),
        c.SkillGroups
            .Select(g => new SkillGroupDto(g.Name, g.Members.ToList()))
            .ToList(),
        c.Sections
            .Select(s => new ResumeSectionDto(
                s.Heading,
                s.Entries.Select(e => new SectionEntryDto(e.Title, e.Lines.ToList())).ToList()))
            .ToList());

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
