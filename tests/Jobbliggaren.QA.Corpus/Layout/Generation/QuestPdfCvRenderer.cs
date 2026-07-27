using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestDocument = QuestPDF.Fluent.Document;

namespace Jobbliggaren.QA.Corpus.Layout.Generation;

/// <summary>
/// Renders REAL PDF bytes for the layout corpus, one method per MECHANIC. Precedent:
/// <c>PdfPigCvLayoutAnalyzerTests</c>, which already synthesizes genuine PDFs with QuestPDF
/// rather than shipping binary fixtures — geometry is only honestly testable against real bytes.
///
/// <para><b>The mechanic that governs every claim here (measured 2026-07-26):</b>
/// <c>ContentOrderTextExtractor</c> linearises by PDF CONTENT-STREAM order, not by page geometry.
/// Blocks placed at y=700, 430, 200, 200, 60 pt came out in the order they were EMITTED, not
/// top-to-bottom. Emission order is therefore a knob the GENERATOR holds, which is exactly what a
/// real-world producer (Word, Canva, InDesign, a LaTeX class) also holds. Two consequences the
/// method names encode: (1) an ordering assertion over extracted text would mostly restate this
/// file, so ordering is RECORDED, never asserted; (2) every method is named for its mechanic and
/// never for a vendor — no genuine vendor export was measured by anyone in this work.</para>
///
/// <para>QuestPDF's font manager is not thread-safe. The project's <c>xunit.runner.json</c> sets
/// <c>parallelizeTestCollections: false</c>, which is what makes this safe here; that is a
/// project-wide setting this class does not own, hence the note. The Community licence is
/// declared once in the static constructor (idempotent; <c>CvRenderer</c> sets it too).</para>
/// </summary>
internal static class QuestPdfCvRenderer
{
    static QuestPdfCvRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    private const float SidebarWidthPoints = 150f;
    private const float PageMarginPoints = 40f;

    /// <summary>The plainest possible chronological CV: one column, headings, blocks in document
    /// order. The point of this mechanic is its ORDINARINESS — if the entry collapse reproduces
    /// here, the defect is a property of the container, not of exotic layout.</summary>
    internal static byte[] SingleColumn(CvModel m) =>
        Render(page => page.Content().Column(col =>
        {
            Identity(col, m);
            Section(col, m.Headings.Profile, m.ProfileLines);
            Experience(col, m);
            Education(col, m);
            Section(col, m.Headings.Skills, m.Skills);
            Section(col, m.Headings.Languages, m.Languages);
            Section(col, m.Headings.UnknownProjects, m.ProjectLines);
        }));

    /// <summary>Geometrically two-column, emitted column-sequentially: QuestPDF's
    /// <c>.Row()</c> emits the sidebar's ENTIRE block before the main column's, so the extracted
    /// text is a flat sequence indistinguishable from a single-column document authoring the same
    /// content in the same order. That is why this case's byte proof is GEOMETRIC (two disjoint
    /// x-bands) and its linearisation order is recorded rather than asserted.</summary>
    internal static byte[] SidebarEmittedFirst(CvModel m) =>
        Render(page => page.Content().Row(row =>
        {
            row.ConstantItem(SidebarWidthPoints).Column(col =>
            {
                Identity(col, m);
                Section(col, m.Headings.Skills, m.Skills);
                Section(col, m.Headings.Languages, m.Languages);
            });
            row.RelativeItem().PaddingLeft(20).Column(col =>
            {
                Section(col, m.Headings.Profile, m.ProfileLines);
                Experience(col, m);
                Education(col, m);
                Section(col, m.Headings.UnknownProjects, m.ProjectLines);
            });
        }));

    /// <summary>The only two-column shape whose two-column-ness the extractor makes VISIBLE: a
    /// Column of Rows, so a sidebar cell and a main cell share every baseline and their runs fuse
    /// onto one output line ("Anna Andersson PROFIL", "C# ARBETSLIVSERFARENHET"). This is a
    /// SEPARATE case from <see cref="SidebarEmittedFirst"/> and carries a separate claim: a
    /// row-interleaved generator fuses baselines. Neither case licenses "a two-column PDF merges
    /// baselines".</summary>
    internal static byte[] InterleavedBaselineFusion(CvModel m)
    {
        var left = SidebarLines(m);
        var right = MainColumnLines(m);
        var rows = Math.Max(left.Count, right.Count);

        return Render(page => page.Content().Column(col =>
        {
            for (var i = 0; i < rows; i++)
            {
                var l = i < left.Count ? left[i] : string.Empty;
                var r = i < right.Count ? right[i] : string.Empty;
                col.Item().Row(row =>
                {
                    row.ConstantItem(SidebarWidthPoints).Text(l);
                    row.RelativeItem().Text(r);
                });
            }
        }));
    }

    /// <summary>A right-aligned period cell abutting a left-aligned company cell with zero
    /// padding, which makes the x-gap non-positive and the runs concatenate WITHOUT a separating
    /// space: "2021 - 2026Senior backend-utvecklare - Klarna AB". This is the second of the three
    /// extraction defects the CTO named as known-remaining after PR E.</summary>
    internal static byte[] ZeroXGapConcat(CvModel m) =>
        Render(page => page.Content().Column(col =>
        {
            Identity(col, m);
            col.Item().Text(m.Headings.Experience);
            foreach (var e in m.Employments)
            {
                col.Item().Row(row =>
                {
                    row.ConstantItem(70).AlignRight().Text(e.Period);
                    row.RelativeItem().Text($"{e.Role} - {e.Marker}");
                });
                col.Item().Text(e.Bullet);
            }

            Education(col, m);
        }));

    /// <summary>
    /// A decorative, NON-SEQUENTIALLY-EMITTED layout: a background fill, a decorative watermark
    /// whose text enters the content stream, and the identity block emitted LAST while positioned
    /// at the page top. The pairing is the measurement — asserted geometry (identity at the top)
    /// against recorded output order (identity late) is what makes "emission order is not
    /// geometric order" a finding rather than a restatement of this file.
    ///
    /// <para><b>Naming discipline, load-bearing.</b> This is a Canva-CLASS form. No genuine Canva
    /// export was run through the extractor by anyone in this work, so the corpus may say "a
    /// decorative, non-sequentially-emitted PDF behaves like this" and may never say "Canva
    /// behaves like this".</para>
    /// </summary>
    internal static byte[] NonSequentialDecorative(CvModel m) =>
        Render(page => page.Content().Layers(layers =>
        {
            layers.Layer().Text("CV 2026").FontSize(48);

            layers.PrimaryLayer().Column(col =>
            {
                col.Item().PaddingTop(90).Text(m.Headings.Profile);
                foreach (var line in m.ProfileLines)
                    col.Item().Text(line);
                Experience(col, m);
                Education(col, m);
                Section(col, m.Headings.Skills, m.Skills);
                Section(col, m.Headings.Languages, m.Languages);
                Section(col, m.Headings.UnknownProjects, m.ProjectLines);
            });

            // Emitted AFTER the primary layer (so it is late in the content stream) but
            // translated back to the page top (so it is early geometrically).
            layers.Layer().OffsetY(-24).Column(col =>
            {
                col.Item().Text(m.PersonName);
                col.Item().Text(m.Email);
                col.Item().Text(m.Phone);
                col.Item().Text(m.City);
            });
        }));

    /// <summary>Single column, with the unknown heading placed IMMEDIATELY AFTER the profile
    /// block. Position is load-bearing and is a byte-level authoring fact: measured 2026-07-26,
    /// the same heading after UTBILDNING is swallowed by Education and after SPRÅK by the
    /// Languages list. "Lands in Summary" is a property of the POSITION, not of the heading.</summary>
    internal static byte[] UnknownHeadingAfterProfile(CvModel m) =>
        HeadingAfterProfile(m, m.Headings.UnknownProjects);

    /// <summary>The paired control: the identical slot with the KNOWN synonym. The pair is what
    /// licenses the claim that the parenthetical defeats recognition — alone, either case is
    /// consistent with several explanations.</summary>
    internal static byte[] KnownHeadingAfterProfile(CvModel m) =>
        HeadingAfterProfile(m, m.Headings.KnownProjects);

    private static byte[] HeadingAfterProfile(CvModel m, string projectHeading) =>
        Render(page => page.Content().Column(col =>
        {
            Identity(col, m);
            Section(col, m.Headings.Profile, m.ProfileLines);
            Section(col, projectHeading, m.ProjectLines);
            Experience(col, m);
            Education(col, m);
            Section(col, m.Headings.Skills, m.Skills);
            Section(col, m.Headings.Languages, m.Languages);
        }));

    /// <summary>No headings at all — the HONEST-FAILURE control. Nothing is silently lost: every
    /// line lands in the preamble and every section reports NotFound. This row is what makes the
    /// Confident-while-truncated rows legible as a defect rather than as ordinary parsing
    /// difficulty.</summary>
    internal static byte[] Headingless(CvModel m) =>
        Render(page => page.Content().Column(col =>
        {
            Identity(col, m);
            foreach (var line in m.ProfileLines)
                col.Item().Text(line);
            foreach (var e in m.Employments)
            {
                col.Item().Text($"{e.Role} - {e.Marker}");
                col.Item().Text(e.Period);
                col.Item().Text(e.Bullet);
            }

            foreach (var e in m.Educations)
                col.Item().Text($"{e.Degree} - {e.Marker} ({e.Period})");
        }));

    /// <summary>A known heading defeated by decorative GLUE ("• ARBETSLIVSERFARENHET").
    /// <c>CvParsingLexiconLoader.NormalizeHeading</c> trims only a trailing colon or period and is
    /// deliberately glue-blind, so a leading bullet defeats an otherwise-known synonym. Its
    /// falsifier is a SOURCE edit where pin P7's is a DATA edit — that is why both are kept.</summary>
    internal static byte[] DecoratedHeadingGlue(CvModel m) =>
        Render(page => page.Content().Column(col =>
        {
            Identity(col, m);
            Section(col, m.Headings.Profile, m.ProfileLines);
            col.Item().Text($"• {m.Headings.Experience}");
            foreach (var e in m.Employments)
                EmploymentLines(col, e);
            Education(col, m);
            Section(col, m.Headings.Skills, m.Skills);
        }));

    /// <summary>A page break MID-EXPERIENCE. This is the only case that touches
    /// <c>PdfPigOpenXmlCvTextExtractor.cs:118</c> — the single '\n' appended at the page seam,
    /// which is half of the cited defect and which no other case exercises.</summary>
    internal static byte[] TwoPageSeam(CvModel m) =>
        Render(page => page.Content().Column(col =>
        {
            Identity(col, m);
            Section(col, m.Headings.Profile, m.ProfileLines);
            col.Item().Text(m.Headings.Experience);
            for (var i = 0; i < m.Employments.Count; i++)
            {
                if (i == 2)
                    col.Item().PageBreak();
                EmploymentLines(col, m.Employments[i]);
            }

            Education(col, m);
            Section(col, m.Headings.Skills, m.Skills);
        }));

    /// <summary>Single column carrying a synthetic personnummer in the contact block. Without it
    /// the personnummer gates are exercised only NEGATIVELY, and deleting the guard call would
    /// leave the whole report byte-identical. The value is never printed to the artifact, a log,
    /// or the committed baseline.</summary>
    internal static byte[] PersonnummerBearing(CvModel m) =>
        Render(page => page.Content().Column(col =>
        {
            Identity(col, m);
            col.Item().Text(m.SyntheticPersonnummer ?? string.Empty);
            Section(col, m.Headings.Profile, m.ProfileLines);
            Experience(col, m);
            Education(col, m);
            Section(col, m.Headings.Skills, m.Skills);
        }));

    // ── shared building blocks ───────────────────────────────────────────────

    private static byte[] Render(Action<PageDescriptor> build) =>
        QuestDocument.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(PageMarginPoints);
            page.DefaultTextStyle(t => t.FontSize(10));
            build(page);
        })).GeneratePdf();

    private static void Identity(ColumnDescriptor col, CvModel m)
    {
        col.Item().Text(m.PersonName);
        col.Item().Text(m.Email);
        col.Item().Text(m.Phone);
        col.Item().Text(m.City);
    }

    private static void Section(ColumnDescriptor col, string heading, IReadOnlyList<string> lines)
    {
        col.Item().Text(heading);
        foreach (var line in lines)
            col.Item().Text(line);
    }

    private static void Experience(ColumnDescriptor col, CvModel m)
    {
        col.Item().Text(m.Headings.Experience);
        foreach (var e in m.Employments)
            EmploymentLines(col, e);
    }

    private static void EmploymentLines(ColumnDescriptor col, EmploymentBlock e)
    {
        col.Item().Text($"{e.Role} - {e.Marker}");
        col.Item().Text(e.Period);
        col.Item().Text(e.Bullet);
    }

    private static void Education(ColumnDescriptor col, CvModel m)
    {
        col.Item().Text(m.Headings.Education);
        foreach (var e in m.Educations)
        {
            col.Item().Text($"{e.Degree} - {e.Marker}");
            col.Item().Text(e.Period);
        }
    }

    private static List<string> SidebarLines(CvModel m)
    {
        List<string> lines = [m.PersonName, m.Email, m.Phone, m.City, m.Headings.Skills];
        lines.AddRange(m.Skills);
        lines.Add(m.Headings.Languages);
        lines.AddRange(m.Languages);
        return lines;
    }

    private static List<string> MainColumnLines(CvModel m)
    {
        List<string> lines = [m.Headings.Profile];
        lines.AddRange(m.ProfileLines);
        lines.Add(m.Headings.Experience);
        foreach (var e in m.Employments)
        {
            lines.Add($"{e.Role} - {e.Marker}");
            lines.Add(e.Period);
            lines.Add(e.Bullet);
        }

        lines.Add(m.Headings.Education);
        foreach (var e in m.Educations)
        {
            lines.Add($"{e.Degree} - {e.Marker}");
            lines.Add(e.Period);
        }

        return lines;
    }
}
