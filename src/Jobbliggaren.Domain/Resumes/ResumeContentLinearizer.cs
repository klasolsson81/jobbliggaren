using System.Text;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// The shared, deterministic linearizer over canonical <see cref="ResumeContent"/>
/// (Fas 4b PR-4, ADR 0093 §D8 — the SPOT for the post-promote citation substrate, the
/// D5e ATS text view and the ATS-PDF composer). A pure function: same content in, same
/// text out — no clock, no I/O, no knowledge-bank data (D3: KB-driven presentation, like
/// localized headings or section reordering, is an OUTER-layer concern composed over
/// this output; it is never baked in here).
/// </summary>
/// <remarks>
/// <para><b>Contract (citation losslessness, CTO-bind D8):</b> every user-authored text
/// unit in the content (names, contact fields, summary, roles/companies, descriptions,
/// institutions/degrees, skill and group names, language names, section headings/titles/
/// lines) appears VERBATIM in <see cref="LinearizedResume.Text"/>, so any quote the
/// review engine cites from the content is locatable in the linear text by ordinal
/// substring search. Measured by <c>ResumeContentLinearizerLosslessnessTests</c>; if that
/// measurement ever trips, the documented fallback is a Form A RawText column (ADR 0093
/// §D8 trip-condition — not built preemptively).</para>
///
/// <para><b>Layout (intrinsic content order):</b> sections in the aggregate's field
/// order — contact, summary, experience, education, skills, languages, then the user's
/// dynamic sections in their own order. Blocks within a section are separated by one
/// blank line; sections by one blank line. Dates render month-granular ISO
/// (<c>yyyy-MM</c>); an open-ended period renders a trailing en dash (<c>yyyy-MM –</c>).
/// Derived, non-user text (date lines) is never a citation target — the engine cites
/// user-authored spans; dates are structured data assessed structurally.</para>
/// </remarks>
public static class ResumeContentLinearizer
{
    private const string BlockSeparator = "\n\n";

    public static LinearizedResume Linearize(ResumeContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var text = new StringBuilder();
        var sections = new List<LinearSection>();

        AppendSection(text, sections, LinearSectionKind.Contact, BuildContact(content.PersonalInfo));

        if (!string.IsNullOrWhiteSpace(content.Summary))
        {
            AppendSection(text, sections, LinearSectionKind.Summary, content.Summary);
        }

        AppendSection(text, sections, LinearSectionKind.Experience, BuildExperience(content.Experiences));
        AppendSection(text, sections, LinearSectionKind.Education, BuildEducation(content.Educations));
        AppendSection(text, sections, LinearSectionKind.Skills, BuildSkills(content.Skills, content.SkillGroups));
        AppendSection(
            text, sections, LinearSectionKind.Languages,
            string.Join('\n', content.Languages.Select(l => l.Name).Where(n => !string.IsNullOrWhiteSpace(n))));

        foreach (var section in content.Sections)
        {
            AppendSection(text, sections, LinearSectionKind.Custom, BuildCustomSection(section));
        }

        return new LinearizedResume(text.ToString(), sections);
    }

    private static void AppendSection(
        StringBuilder text, List<LinearSection> sections, LinearSectionKind kind, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        if (text.Length > 0)
        {
            text.Append(BlockSeparator);
        }

        var start = text.Length;
        text.Append(body);
        sections.Add(new LinearSection(kind, start, body.Length));
    }

    private static string BuildContact(PersonalInfo info)
    {
        string?[] lines = [info.FullName, info.Email, info.Phone, info.Location];
        return string.Join('\n', lines.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    private static string BuildExperience(IReadOnlyList<Experience> experiences)
    {
        var blocks = experiences.Select(e =>
        {
            var lines = new List<string> { e.Role, e.Company };
            if (PeriodLine(e.StartDate, e.EndDate, e.RawPeriod) is { } period)
            {
                lines.Add(period);
            }

            if (!string.IsNullOrWhiteSpace(e.Description))
            {
                lines.Add(e.Description);
            }

            return string.Join('\n', lines);
        });

        return string.Join(BlockSeparator, blocks);
    }

    private static string BuildEducation(IReadOnlyList<Education> educations)
    {
        var blocks = educations.Select(e =>
        {
            var lines = new List<string> { e.Degree, e.Institution };
            if (PeriodLine(e.StartDate, e.EndDate, e.RawPeriod) is { } period)
            {
                lines.Add(period);
            }

            return string.Join('\n', lines);
        });

        return string.Join(BlockSeparator, blocks);
    }

    private static string BuildSkills(
        IReadOnlyList<Skill> skills, IReadOnlyList<SkillGroup> groups)
    {
        var lines = skills
            .Select(s => s.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        // Grouped-skills overlay (ADR 0095 D-A): the group NAME is user-authored text and
        // must be citable; members re-list flat-skill names (duplication is presentation,
        // the flat list above stays authoritative).
        lines.AddRange(groups
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => g.Members.Count > 0 ? $"{g.Name}: {string.Join(", ", g.Members)}" : g.Name));

        return string.Join('\n', lines);
    }

    private static string BuildCustomSection(ResumeSection section)
    {
        var lines = new List<string> { section.Heading };
        foreach (var entry in section.Entries)
        {
            // #815: a titleless entry contributes NO line here — it must not contribute an empty
            // one. string.Join would render a null/blank title as an empty string, i.e. a BLANK
            // LINE, and a blank line is precisely the block separator ("\n\n") that the citation
            // substrate and the review rules split on (ADR 0093 §D8). An unguarded null title would
            // therefore inject a phantom block boundary into the evidence a CV verdict is cited
            // from. Skipping it keeps D8 losslessness intact: a heading that does not exist
            // contributes zero citable units.
            if (!string.IsNullOrWhiteSpace(entry.Title))
                lines.Add(entry.Title);

            lines.AddRange(entry.Lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        }

        return string.Join('\n', lines);
    }

    // Month-granular ISO period line; an ongoing role keeps an open right side. The en
    // dash (U+2013) matches the house period notation (PeriodParser recognises it);
    // em dash is banned in rendered copy (EmDashInReviewCopyGuardTests).
    // Honest date absence (CTO-bind 5a-pre): structured dates are authoritative when a
    // start exists; otherwise the verbatim RawPeriod is the citable fallback; with neither,
    // the entry contributes NO period line — never an empty one (#815 parity: an absent
    // line must not inject a phantom block boundary into the citation substrate).
    private static string? PeriodLine(DateOnly? start, DateOnly? end, string? rawPeriod)
    {
        if (start is { } s)
            return end is { } e ? $"{s:yyyy-MM} – {e:yyyy-MM}" : $"{s:yyyy-MM} –";

        return string.IsNullOrWhiteSpace(rawPeriod) ? null : rawPeriod;
    }
}
