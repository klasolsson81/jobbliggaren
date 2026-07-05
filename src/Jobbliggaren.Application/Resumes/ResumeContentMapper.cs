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

        // Fas 4b AppCopy superset (ADR 0095). The new collections are optional on the
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

    /// <summary>
    /// The inverse projection (Fas 4b PR-7, #656; CTO D-F): maps server-COMPOSED domain
    /// content back to the transport shape so the ONE personnummer guard surface
    /// (<c>ResumeContentPersonnummerGuard.Check(ResumeContentDto)</c>) also covers a
    /// TargetId-based apply — no second guard enumeration to drift (DRY on the
    /// highest-priority invariant). Losslessness for every free-text field is pinned by
    /// the ToDto→ToDomain round-trip test; a field added to one side without the other
    /// fails that pin, not silently.
    /// </summary>
    public static ResumeContentDto ToDto(ResumeContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ResumeContentDto(
            new PersonalInfoDto(
                content.PersonalInfo.FullName,
                content.PersonalInfo.Email,
                content.PersonalInfo.Phone,
                content.PersonalInfo.Location),
            content.Experiences
                .Select(e => new ExperienceDto(e.Company, e.Role, e.StartDate, e.EndDate, e.Description))
                .ToList(),
            content.Educations
                .Select(e => new EducationDto(e.Institution, e.Degree, e.StartDate, e.EndDate))
                .ToList(),
            content.Skills
                .Select(s => new SkillDto(s.Name, s.YearsExperience))
                .ToList(),
            content.Summary,
            content.Languages
                .Select(l => new SpokenLanguageDto(l.Name, l.Proficiency.Name))
                .ToList(),
            content.SkillGroups
                .Select(g => new SkillGroupDto(g.Name, g.Members))
                .ToList(),
            content.Sections
                .Select(s => new ResumeSectionDto(
                    s.Heading,
                    s.Entries.Select(e => new SectionEntryDto(e.Title, e.Lines)).ToList()))
                .ToList());
    }

    private static LanguageProficiency ToProficiency(string? token) =>
        token is not null
        && LanguageProficiency.TryFromName(token, ignoreCase: true, out var proficiency)
            ? proficiency
            : LanguageProficiency.NotStated;
}
