using Jobbliggaren.Application.Resumes.Review.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// Composes a CV PDF page from the single <see cref="CvDocumentModel"/> source (Fas 4 STEG 10,
/// F4-10) — the SAME content for both profiles (BUILD §8.3 "samma JSON-källdata"); only the
/// styling differs (ATS-plain = single column, black, no graphics — maximally machine-parseable;
/// Visual = accent-coloured headings + a thin accent rule, from the WCAG-guarded palette). Only
/// the section LABELS are localised (<see cref="CvRenderStrings"/>); the user's CV content is
/// rendered verbatim — never translated or synthesised (CLAUDE.md §5). All fields are
/// nullable/empty-tolerant: a degraded parse renders an honest partial CV, never a placeholder.
/// </summary>
/// <remarks>
/// PR-8b (8b.0) renders the FULL AppCopy superset so no field is silently dropped before the
/// template work builds on it (P2/P5): grouped skills (a group row plus any UNGROUPED remainder —
/// every skill appears at least once, no loss), spoken-language proficiency (appended to the name,
/// omitted when unknown), and dynamic profession-driven sections (verbatim user headings/entries,
/// always shown — P4). Section ORDERING for the profession-driven sections is a later slice
/// (8b.4a, SSYK reorder); this composer appends them deterministically after the standard sections.
/// </remarks>
internal static class CvDocumentComposer
{
    public static void Compose(
        IDocumentContainer container,
        CvDocumentModel model,
        CvRenderStrings.Labels labels,
        RenderProfile profile)
    {
        var body = CvPalette.Hex(CvPalette.BodyInk);
        var secondary = CvPalette.Hex(CvPalette.Secondary);
        var visual = profile == RenderProfile.Visual;
        var heading = visual ? CvPalette.Hex(CvPalette.Accent) : body;

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            // Pin the font explicitly to the QuestPDF-bundled Lato (an SDK asset, not a system
            // font) — covers åäö and keeps the output deterministic + cross-platform (CI) stable
            // regardless of host fonts or a future QuestPDF default change.
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(body).FontFamily(Fonts.Lato));

            page.Content().Column(col =>
            {
                col.Spacing(6);

                if (!string.IsNullOrWhiteSpace(model.FullName))
                {
                    col.Item().Text(model.FullName).FontSize(18).SemiBold().FontColor(body);
                }

                var contact = Join("  ·  ", model.Email, model.Phone, model.Location);
                if (contact.Length > 0)
                {
                    col.Item().Text(contact).FontSize(9).FontColor(secondary);
                }

                if (visual)
                {
                    col.Item().PaddingTop(2).LineHorizontal(1).LineColor(heading);
                }

                if (!string.IsNullOrWhiteSpace(model.Profile))
                {
                    Section(col, labels.Profile, heading);
                    col.Item().Text(model.Profile);
                }

                if (model.Experiences.Count > 0)
                {
                    Section(col, labels.Experience, heading);
                    foreach (var exp in model.Experiences)
                    {
                        Entry(col, Join(" · ", exp.Title, exp.Organization), exp.Period, exp.Text, secondary);
                    }
                }

                if (model.Educations.Count > 0)
                {
                    Section(col, labels.Education, heading);
                    foreach (var edu in model.Educations)
                    {
                        Entry(col, Join(" · ", edu.Degree, edu.Institution), edu.Period, edu.Text, secondary);
                    }
                }

                SkillsSection(col, labels.Skills, model.Skills, model.SkillGroups, heading);
                LanguagesSection(col, labels.Languages, model.Languages, heading);
                DynamicSections(col, model.Sections, heading, secondary);
            });
        });
    }

    private static void Section(ColumnDescriptor col, string label, string headingColor) =>
        col.Item().PaddingTop(8).Text(label).FontSize(12).SemiBold().FontColor(headingColor);

    private static void Entry(
        ColumnDescriptor col, string title, string? period, string text, string secondaryColor)
    {
        if (title.Length > 0 || !string.IsNullOrWhiteSpace(period))
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(title).SemiBold();
                if (!string.IsNullOrWhiteSpace(period))
                {
                    row.ConstantItem(130).AlignRight().Text(period).FontSize(9).FontColor(secondaryColor);
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            col.Item().Text(text);
        }
    }

    /// <summary>
    /// Renders the Kompetenser section: each skill GROUP as a "Grupp: post, post" row (ATS spec §5.6
    /// rule 9) followed by any UNGROUPED skill as a plain trailing list. The ungrouped remainder is
    /// the authoritative flat set minus every name already shown in a group — so every skill appears
    /// at least once and none is dropped (P2/P5, no content loss). Omitted only when there is nothing
    /// to show.
    /// </summary>
    private static void SkillsSection(
        ColumnDescriptor col,
        string label,
        IReadOnlyList<string> skills,
        IReadOnlyList<CvDocumentModel.SkillGroupLine> groups,
        string headingColor)
    {
        var renderableGroups = groups
            .Select(g => (g.Name, Members: g.Members.Where(m => !string.IsNullOrWhiteSpace(m)).ToList()))
            .Where(g => g.Members.Count > 0)
            .ToList();

        // Ungrouped remainder — the authoritative skills not already shown in any group (no double
        // render, no loss). Ordinal match mirrors the SkillGroup membership invariant (member ∈ Skills[].Name).
        var grouped = renderableGroups.SelectMany(g => g.Members).ToHashSet(StringComparer.Ordinal);
        var ungrouped = skills
            .Where(s => !string.IsNullOrWhiteSpace(s) && !grouped.Contains(s))
            .ToList();

        if (renderableGroups.Count == 0 && ungrouped.Count == 0)
        {
            return;
        }

        Section(col, label, headingColor);

        foreach (var (name, members) in renderableGroups)
        {
            col.Item().Text(text =>
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    text.Span($"{name}: ").SemiBold();
                }
                text.Span(string.Join(", ", members));
            });
        }

        if (ungrouped.Count > 0)
        {
            col.Item().Text(string.Join(", ", ungrouped));
        }
    }

    /// <summary>
    /// Renders the Språk section — each language name with its localised proficiency appended in
    /// parentheses when known (e.g. "Svenska (Modersmål)"); an unknown level renders the bare name
    /// (never a fabricated level — ADR 0074 OQ3 / §5). Omitted when empty (honest partial).
    /// </summary>
    private static void LanguagesSection(
        ColumnDescriptor col,
        string label,
        IReadOnlyList<CvDocumentModel.LanguageLine> languages,
        string headingColor)
    {
        var values = languages
            .Where(l => !string.IsNullOrWhiteSpace(l.Name))
            .Select(l => string.IsNullOrWhiteSpace(l.Proficiency) ? l.Name : $"{l.Name} ({l.Proficiency})")
            .ToList();

        if (values.Count == 0)
        {
            return;
        }

        Section(col, label, headingColor);
        col.Item().Text(string.Join(", ", values));
    }

    /// <summary>
    /// Renders the dynamic profession-driven sections (Projekt, Legitimation, Referenser, …) verbatim
    /// — the user's heading (never a SmartEnum, always shown, P4), each entry's title in semibold and
    /// its body lines beneath. Their ordering is a later slice (8b.4a); here they append after the
    /// standard sections in content order. A section with neither heading text nor entries is skipped.
    /// </summary>
    private static void DynamicSections(
        ColumnDescriptor col,
        IReadOnlyList<CvDocumentModel.SectionLine> sections,
        string headingColor,
        string secondaryColor)
    {
        foreach (var section in sections)
        {
            var entries = section.Entries;
            var hasHeading = !string.IsNullOrWhiteSpace(section.Heading);
            if (!hasHeading && entries.Count == 0)
            {
                continue;
            }

            if (hasHeading)
            {
                Section(col, section.Heading, headingColor);
            }

            foreach (var entry in entries)
            {
                var lines = entry.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (string.IsNullOrWhiteSpace(entry.Title) && lines.Count == 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entry.Title))
                {
                    col.Item().Text(entry.Title).SemiBold();
                }

                foreach (var line in lines)
                {
                    col.Item().Text(line);
                }
            }
        }
    }

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
