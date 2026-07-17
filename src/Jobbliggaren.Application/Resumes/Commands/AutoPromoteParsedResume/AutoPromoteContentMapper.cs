using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;

/// <summary>
/// The bound verbatim projection <c>ParsedResumeContent → ResumeContentDto</c> (CV-pivot
/// PR 5a mapping table, CTO-bind 2026-07-17 §1). Projects to the TRANSPORT shape — not the
/// domain — so auto-promote funnels through the exact same promote pipeline as user-promote
/// (personnummer guard on the DTO → <c>ResumeContentMapper.ToDomain</c> →
/// <c>Resume.CreateFromParsed</c>), keeping ONE guard surface and ONE validation authority.
///
/// <para>Pure projection, three bound policies, zero synthesis (ADR 0071/CLAUDE.md §5):</para>
/// <list type="bullet">
/// <item><b>Name:</b> <paramref name="fullName"/> is the resolved ACCOUNT name — the parsed
/// <c>Contact.FullName</c> is never used (Klas-bound 2026-07-16).</item>
/// <item><b>Dates:</b> the parse carries only loose period strings, so structured dates are
/// honestly absent (null/null) and the verbatim <c>Period</c> rides <c>RawPeriod</c>
/// UNTRUNCATED — an over-long period is for the buildability gate to reject, not for this
/// projection to silently shorten (#914 honest-date-absence).</item>
/// <item><b>Description = null:</b> the segmenter's <c>RawText</c> is the WHOLE entry block
/// (title/org/period lines included) — promoting it as a description would double those
/// lines in render and feed them to the review engine as <c>TextIsDescriptionOnly</c> prose
/// (scoring corruption), and computing the residual would be re-parsing the engine never
/// does. The parse has no structured description, so the canonical entry honestly has none;
/// the raw text stays citable on the retained staging artifact (CTO-bind §1 fork (a)).</item>
/// </list>
///
/// <para>Entries are NEVER filtered or dropped: an entry missing its organization projects
/// with an empty company (identical under <c>ValidateContent</c>'s <c>IsNullOrWhiteSpace</c>)
/// and the buildability gate turns it into an honest <c>LeftPending(IncompleteContent)</c> —
/// silently dropping the entry would promote a CV that says less than the file did.</para>
/// </summary>
internal static class AutoPromoteContentMapper
{
    public static ResumeContentDto ToContentDto(ParsedResumeContent parsed, string fullName)
    {
        var contact = parsed.Contact;

        var experiences = parsed.Experience
            .Select(e => new ExperienceDto(
                Company: e.Organization ?? string.Empty,
                Role: e.Title ?? string.Empty,
                StartDate: null,
                EndDate: null,
                Description: null,
                RawPeriod: e.Period))
            .ToList();

        var educations = parsed.Education
            .Select(e => new EducationDto(
                Institution: e.Institution ?? string.Empty,
                Degree: e.Degree ?? string.Empty,
                StartDate: null,
                EndDate: null,
                RawPeriod: e.Period))
            .ToList();

        var skills = parsed.Skills
            .Select(name => new SkillDto(name, YearsExperience: null))
            .ToList();

        // Import parsing yields language NAMES only — an unknown level is unknown, not
        // "basic" (LanguageProficiency.NotStated, ADR 0074 OQ3 honesty).
        var languages = parsed.Languages
            .Select(name => new SpokenLanguageDto(name, LanguageProficiency.NotStated.Name))
            .ToList();

        var sections = parsed.Sections
            .Select(s => new ResumeSectionDto(
                s.Heading,
                s.Entries
                    .Select(e => new SectionEntryDto(e.Title, e.Lines))
                    .ToList()))
            .ToList();

        // Preamble is deliberately NOT mapped: the policy gate has already blocked any parse
        // that carries one (ADR 0109 — only the user classifies unheaded text), so by the
        // time this projection runs there is nothing to carry. SkillGroups: the parse has
        // no grouping concept — an empty overlay, never an invented one.
        return new ResumeContentDto(
            new PersonalInfoDto(fullName, contact.Email, contact.Phone, contact.Location),
            experiences,
            educations,
            skills,
            Summary: parsed.Profile,
            Languages: languages,
            SkillGroups: [],
            Sections: sections);
    }
}
