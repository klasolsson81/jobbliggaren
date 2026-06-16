using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes;

/// <summary>
/// Maps a transport <see cref="ResumeContentDto"/> to the domain <see cref="ResumeContent"/>
/// value object. Shared by the write surfaces that accept a full structured payload
/// (<c>UpdateMasterContent</c> and <c>PromoteParsedResume</c> — CTO DQ1 Variant A / DQ2,
/// Application-layer mapping). Pure projection — no validation (the aggregate's
/// <c>ValidateContent</c> owns that), no synthesis (CLAUDE.md §5).
/// </summary>
internal static class ResumeContentMapper
{
    public static ResumeContent ToDomain(ResumeContentDto dto)
    {
        var personalInfo = new PersonalInfo(
            dto.PersonalInfo.FullName,
            dto.PersonalInfo.Email,
            dto.PersonalInfo.Phone,
            dto.PersonalInfo.Location);

        var experiences = dto.Experiences
            .Select(e => new Experience(e.Company, e.Role, e.StartDate, e.EndDate, e.Description))
            .ToList();

        var educations = dto.Educations
            .Select(e => new Education(e.Institution, e.Degree, e.StartDate, e.EndDate))
            .ToList();

        var skills = dto.Skills
            .Select(s => new Skill(s.Name, s.YearsExperience))
            .ToList();

        return new ResumeContent(personalInfo, experiences, educations, skills, dto.Summary);
    }
}
