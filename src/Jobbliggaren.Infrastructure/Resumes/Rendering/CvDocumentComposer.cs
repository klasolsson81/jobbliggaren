using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// Composes a CV PDF from the single <see cref="CvDocumentModel"/> source (Fas 4 STEG 10 / PR-8b) —
/// the SAME content across every template and profile (BUILD §8.3 "samma JSON-källdata"); only the
/// layout + styling differ. Three visual templates (design handoff §8) render on the
/// <see cref="RenderProfile.Visual"/> profile: <b>Klar</b> (single-column, thin accent line, uppercase
/// underlined headings — the ATS-safe default), <b>Accentlinje</b> (single-column, an accent bar
/// before each heading, accent-coloured skill-group labels), and <b>MorkPanel</b> (two-column: an
/// accent-coloured side panel with contact + skills + languages, and a light main column). The
/// <see cref="RenderProfile.Ats"/> profile ALWAYS renders the plain single-column parallel from the
/// same content regardless of the chosen template — so a two-column choice never costs a parseable
/// version (§5.5/§8, honesty). Accent comes from the WCAG-guarded <see cref="CvPalette"/>; density
/// scales the spacing (Klas 2026-07-12: percentage-scale one base, 0.85/1.0/1.2). Only section LABELS
/// are localised; the user's content is rendered verbatim — never translated/synthesised (§5). No
/// content is ever dropped (grouped skills keep an ungrouped remainder; every field renders — P2/P5).
/// </summary>
/// <remarks>
/// Font pair (Modern/Klassisk) is threaded but renders with the QuestPDF-bundled Lato for BOTH values
/// today — the Modern (Source Sans 3) / Classic (serif) distinction awaits an embedded OFL serif asset
/// (a flagged follow-up); Lato covers åäö and is deterministic. Photo controls are stored but no photo
/// region is emitted (deferred to PR-10, DPIA gate). MorkPanel's side panel and main column both flow
/// across pages (QuestPDF paginates the Row; the panel background extends per page) — verified against
/// a maximal, multi-page fixture so a long CV never throws <c>DocumentLayoutException</c> or clips.
/// </remarks>
internal static class CvDocumentComposer
{
    private enum Variant { Ats, Klar, Accentlinje }

    // Density → spacing multiplier (Klas 2026-07-12: percentage-scale one base spacing).
    private static float DensityScale(CvDensity density) =>
        density == CvDensity.Compact ? 0.85f : density == CvDensity.Airy ? 1.2f : 1.0f;

    public static void Compose(
        IDocumentContainer container,
        CvDocumentModel model,
        CvRenderStrings.Labels labels,
        CvTemplateOptions options,
        RenderProfile profile)
    {
        var accent = CvPalette.Hex(CvPalette.Accent(options.AccentColor));
        var scale = DensityScale(options.Density);
        // FontPair interim: both Modern and Classic use the QuestPDF-bundled Lato (the only embedded
        // font; covers åäö, deterministic). The serif (Classic) awaits an OFL font asset — threaded now
        // so no re-plumbing later, rendered uniformly until then (parity the deferred photo controls).
        var font = Fonts.Lato;

        // The ATS profile always renders the plain single-column parallel, IGNORING the visual
        // template — the ATS-safe version is generated in parallel from the same content (§5.5/§8).
        if (profile == RenderProfile.Ats)
        {
            ComposeSingleColumn(container, model, labels, Variant.Ats, accent, font, scale);
            return;
        }

        switch (options.Template.Name)
        {
            case nameof(CvTemplate.MorkPanel):
                ComposeMorkPanel(container, model, labels, accent, font, scale);
                break;
            case nameof(CvTemplate.Accentlinje):
                ComposeSingleColumn(container, model, labels, Variant.Accentlinje, accent, font, scale);
                break;
            default: // Klar (and any future single-column template)
                ComposeSingleColumn(container, model, labels, Variant.Klar, accent, font, scale);
                break;
        }
    }

    // ----------------------------------------------------------------------
    // Single-column layouts — Ats (plain), Klar, Accentlinje. Same content,
    // same order; only the header rule + heading treatment differ.
    // ----------------------------------------------------------------------

    private static void ComposeSingleColumn(
        IDocumentContainer container,
        CvDocumentModel model,
        CvRenderStrings.Labels labels,
        Variant variant,
        string accent,
        string font,
        float scale)
    {
        var body = CvPalette.Hex(CvPalette.BodyInk);
        var secondary = CvPalette.Hex(CvPalette.Secondary);
        // Visual templates colour headings with the accent; the ATS parallel stays plain ink.
        var headingColor = variant == Variant.Ats ? body : accent;
        // Accentlinje tints skill-group labels with the accent ("färgade teknikrader"); others keep ink.
        var groupNameColor = variant == Variant.Accentlinje ? accent : body;

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(body).FontFamily(font));

            page.Content().Column(col =>
            {
                col.Spacing(scale * 6);

                if (!string.IsNullOrWhiteSpace(model.FullName))
                {
                    col.Item().Text(model.FullName).FontSize(18).SemiBold().FontColor(body);
                }

                var contact = Join("  ·  ", model.Email, model.Phone, model.Location);
                if (contact.Length > 0)
                {
                    col.Item().Text(contact).FontSize(9).FontColor(secondary);
                }

                // Klar: a thin accent line under the header (handoff §8 "namn + tunn accentlinje").
                if (variant == Variant.Klar)
                {
                    col.Item().PaddingTop(scale * 2).LineHorizontal(1).LineColor(accent);
                }

                void Heading(string text) => SingleColumnHeading(col, text, variant, headingColor, scale);

                if (!string.IsNullOrWhiteSpace(model.Profile))
                {
                    Heading(labels.Profile);
                    col.Item().Text(model.Profile);
                }

                ExperienceSection(col, labels.Experience, model.Experiences, Heading, secondary, body);
                EducationSection(col, labels.Education, model.Educations, Heading, secondary, body);
                SkillsSection(col, labels.Skills, model.Skills, model.SkillGroups, Heading, groupNameColor, body);
                LanguagesSection(col, labels.Languages, model.Languages, Heading, body);
                DynamicSections(col, model.Sections, Heading, body);
            });
        });
    }

    private static void SingleColumnHeading(
        ColumnDescriptor col, string text, Variant variant, string color, float scale)
    {
        switch (variant)
        {
            case Variant.Klar:
                // Uppercase heading with a thin underline directly beneath (tight — one item).
                col.Item().PaddingTop(scale * 8).Column(h =>
                {
                    h.Item().Text(text.ToUpperInvariant()).FontSize(11).SemiBold().FontColor(color);
                    h.Item().PaddingTop(1).LineHorizontal(0.75f).LineColor(color);
                });
                break;

            case Variant.Accentlinje:
                // A short accent bar before the heading text (handoff §8 "färgat streck före rubriker").
                col.Item().PaddingTop(scale * 8).Row(row =>
                {
                    row.ConstantItem(4).Background(color);
                    row.ConstantItem(7);
                    row.AutoItem().AlignMiddle().Text(text).FontSize(12).SemiBold().FontColor(color);
                });
                break;

            default: // Ats — plain, machine-parseable standard heading.
                col.Item().PaddingTop(scale * 8).Text(text).FontSize(12).SemiBold().FontColor(color);
                break;
        }
    }

    // ----------------------------------------------------------------------
    // Two-column MorkPanel — accent side panel (contact + skills + languages,
    // white text) + light main column (name, profile, experience, education,
    // sections). Both flow across pages; the panel background extends per page.
    // ----------------------------------------------------------------------

    private static void ComposeMorkPanel(
        IDocumentContainer container,
        CvDocumentModel model,
        CvRenderStrings.Labels labels,
        string accent,
        string font,
        float scale)
    {
        var body = CvPalette.Hex(CvPalette.BodyInk);
        var secondary = CvPalette.Hex(CvPalette.Secondary);
        var panelText = CvPalette.Hex(CvPalette.PanelText);

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0); // the side panel runs edge-to-edge; content padding is applied per column.
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(body).FontFamily(font));

            page.Content().Row(row =>
            {
                // Left: accent side panel, full page height, white text. ExtendVertical fills each page.
                row.ConstantItem(188)
                    .Background(accent)
                    .ExtendVertical()
                    .PaddingVertical(scale * 26)
                    .PaddingHorizontal(scale * 18)
                    .Column(panel =>
                    {
                        panel.Spacing(scale * 5);

                        void PanelHeading(string text) => panel.Item()
                            .PaddingTop(scale * 10).Text(text.ToUpperInvariant())
                            .FontSize(10).SemiBold().FontColor(panelText);

                        var contactLines = new[] { model.Email, model.Phone, model.Location }
                            .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                        if (contactLines.Count > 0)
                        {
                            // First panel block: no PaddingTop offset (it opens the panel).
                            panel.Item().Text(labels.Contact.ToUpperInvariant())
                                .FontSize(10).SemiBold().FontColor(panelText);
                            foreach (var line in contactLines)
                            {
                                panel.Item().Text(line).FontSize(9).FontColor(panelText);
                            }
                        }

                        // In the panel, headings + text are white; group labels stay white (accent on
                        // accent would be invisible).
                        SkillsSection(panel, labels.Skills, model.Skills, model.SkillGroups, PanelHeading, panelText, panelText);
                        LanguagesSection(panel, labels.Languages, model.Languages, PanelHeading, panelText);
                    });

                // Right: light main column, ink text.
                row.RelativeItem()
                    .PaddingVertical(scale * 26)
                    .PaddingHorizontal(scale * 22)
                    .Column(main =>
                    {
                        main.Spacing(scale * 6);

                        if (!string.IsNullOrWhiteSpace(model.FullName))
                        {
                            main.Item().Text(model.FullName).FontSize(20).SemiBold().FontColor(body);
                        }

                        void MainHeading(string text) => main.Item()
                            .PaddingTop(scale * 8).Text(text).FontSize(12).SemiBold().FontColor(accent);

                        if (!string.IsNullOrWhiteSpace(model.Profile))
                        {
                            MainHeading(labels.Profile);
                            main.Item().Text(model.Profile);
                        }

                        ExperienceSection(main, labels.Experience, model.Experiences, MainHeading, secondary, body);
                        EducationSection(main, labels.Education, model.Educations, MainHeading, secondary, body);
                        DynamicSections(main, model.Sections, MainHeading, body);
                    });
            });
        });
    }

    // ----------------------------------------------------------------------
    // Section renderers — colour-parameterised so they serve both the ink
    // single-column/main flows and the white MorkPanel. Content logic is the
    // 8b.0 logic (no field dropped; grouped skills keep an ungrouped remainder).
    // ----------------------------------------------------------------------

    private static void ExperienceSection(
        ColumnDescriptor col, string label, IReadOnlyList<CvDocumentModel.ExperienceLine> items,
        Action<string> heading, string secondaryColor, string bodyColor)
    {
        if (items.Count == 0)
        {
            return;
        }

        heading(label);
        foreach (var exp in items)
        {
            Entry(col, Join(" · ", exp.Title, exp.Organization), exp.Period, exp.Text, secondaryColor, bodyColor);
        }
    }

    private static void EducationSection(
        ColumnDescriptor col, string label, IReadOnlyList<CvDocumentModel.EducationLine> items,
        Action<string> heading, string secondaryColor, string bodyColor)
    {
        if (items.Count == 0)
        {
            return;
        }

        heading(label);
        foreach (var edu in items)
        {
            Entry(col, Join(" · ", edu.Degree, edu.Institution), edu.Period, edu.Text, secondaryColor, bodyColor);
        }
    }

    private static void Entry(
        ColumnDescriptor col, string title, string? period, string text, string secondaryColor, string bodyColor)
    {
        if (title.Length > 0 || !string.IsNullOrWhiteSpace(period))
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(title).SemiBold().FontColor(bodyColor);
                if (!string.IsNullOrWhiteSpace(period))
                {
                    row.ConstantItem(130).AlignRight().Text(period).FontSize(9).FontColor(secondaryColor);
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            col.Item().Text(text).FontColor(bodyColor);
        }
    }

    /// <summary>
    /// Kompetenser: each skill GROUP as a "Grupp: post, post" row (ATS §5.6 rule 9) followed by any
    /// UNGROUPED skill — the authoritative flat set minus every name already shown in a group, so every
    /// skill appears at least once and none is dropped (P2/P5). Omitted only when empty.
    /// </summary>
    private static void SkillsSection(
        ColumnDescriptor col, string label, IReadOnlyList<string> skills,
        IReadOnlyList<CvDocumentModel.SkillGroupLine> groups, Action<string> heading,
        string groupNameColor, string bodyColor)
    {
        var renderableGroups = groups
            .Select(g => (g.Name, Members: g.Members.Where(m => !string.IsNullOrWhiteSpace(m)).ToList()))
            .Where(g => g.Members.Count > 0)
            .ToList();

        var grouped = renderableGroups.SelectMany(g => g.Members).ToHashSet(StringComparer.Ordinal);
        var ungrouped = skills
            .Where(s => !string.IsNullOrWhiteSpace(s) && !grouped.Contains(s))
            .ToList();

        if (renderableGroups.Count == 0 && ungrouped.Count == 0)
        {
            return;
        }

        heading(label);

        foreach (var (name, members) in renderableGroups)
        {
            col.Item().Text(text =>
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    text.Span($"{name}: ").SemiBold().FontColor(groupNameColor);
                }
                text.Span(string.Join(", ", members)).FontColor(bodyColor);
            });
        }

        if (ungrouped.Count > 0)
        {
            col.Item().Text(string.Join(", ", ungrouped)).FontColor(bodyColor);
        }
    }

    private static void LanguagesSection(
        ColumnDescriptor col, string label, IReadOnlyList<CvDocumentModel.LanguageLine> languages,
        Action<string> heading, string bodyColor)
    {
        var values = languages
            .Where(l => !string.IsNullOrWhiteSpace(l.Name))
            .Select(l => string.IsNullOrWhiteSpace(l.Proficiency) ? l.Name : $"{l.Name} ({l.Proficiency})")
            .ToList();

        if (values.Count == 0)
        {
            return;
        }

        heading(label);
        col.Item().Text(string.Join(", ", values)).FontColor(bodyColor);
    }

    /// <summary>
    /// Dynamic profession-driven sections (Projekt, Legitimation, Referenser, …) verbatim — the user's
    /// heading (always shown, P4), each entry's title in semibold and its body lines beneath. Ordering
    /// is a later slice (8b.4a); here they append in content order. A section with neither heading text
    /// nor entries is skipped.
    /// </summary>
    private static void DynamicSections(
        ColumnDescriptor col, IReadOnlyList<CvDocumentModel.SectionLine> sections,
        Action<string> heading, string bodyColor)
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
                heading(section.Heading);
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
                    col.Item().Text(entry.Title).SemiBold().FontColor(bodyColor);
                }

                foreach (var line in lines)
                {
                    col.Item().Text(line).FontColor(bodyColor);
                }
            }
        }
    }

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
