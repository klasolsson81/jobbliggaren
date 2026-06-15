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

                ListSection(col, labels.Skills, model.Skills, heading);
                ListSection(col, labels.Languages, model.Languages, heading);
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

    private static void ListSection(
        ColumnDescriptor col, string label, IReadOnlyList<string> items, string headingColor)
    {
        var values = items.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (values.Count == 0)
        {
            return;
        }

        Section(col, label, headingColor);
        col.Item().Text(string.Join(", ", values));
    }

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
