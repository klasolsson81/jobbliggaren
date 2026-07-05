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

        // Fas 4b AppCopy superset (ADR 0094). The new collections are optional on the
        // transport (a pre-superset client omits them → null); coalesce to empty — the
        // NESTED lists too (STJ passes null for an omitted member; a partial-but-parseable
        // payload must map, not NRE→500). An unknown/absent proficiency token maps to
        // NotStated (tolerant, never synthesised — CLAUDE.md §5; the level is the user's
        // to state).
        var languages = (dto.Languages ?? [])
            .Select(l => new SpokenLanguage(l.Name, ToProficiency(l.Proficiency)))
            .ToList();

        var skillGroups = (dto.SkillGroups ?? [])
            .Select(g => new SkillGroup(g.Name, (g.Members ?? []).ToList()))
            .ToList();

        var sections = (dto.Sections ?? [])
            .Select(s => new ResumeSection(
                s.Heading,
                (s.Entries ?? [])
                    .Select(e => new SectionEntry(e.Title, (e.Lines ?? []).ToList()))
                    .ToList()))
            .ToList();

        return new ResumeContent(
            personalInfo, experiences, educations, skills, dto.Summary,
            languages, skillGroups, sections);
    }

    private static LanguageProficiency ToProficiency(string? token) =>
        token is not null
        && LanguageProficiency.TryFromName(token, ignoreCase: true, out var proficiency)
            ? proficiency
            : LanguageProficiency.NotStated;
}
