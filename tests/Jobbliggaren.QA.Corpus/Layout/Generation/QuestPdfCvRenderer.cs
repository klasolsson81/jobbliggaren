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

    /// <summary>
    /// Word's documented default paragraph spacing (8 pt after a Normal paragraph). Used by the
    /// two SPACED cases and by nothing else.
    ///
    /// <para><b>Why this number and not another (#1060 PR E).</b> Every other case in this file
    /// authors NO vertical spacing whatsoever — measured 2026-07-27, all thirteen PDF cases emit
    /// exactly one inter-baseline gap value (12.0 pt) across every page. That was invisible until
    /// PR E needed it, and it made the corpus unable to distinguish "the extractor discards the
    /// paragraph boundary" from "the document never had one". This value is taken from the word
    /// processor most Swedish CVs are written in, NOT chosen by trying values until a downstream
    /// rule fires; the rule that reads it must never appear in this file.</para>
    /// </summary>
    private const float WordDefaultParagraphSpacingPoints = 8f;

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

    /// <summary>
    /// The same chronological CV as <see cref="SingleColumn"/>, authored the way a word processor
    /// actually lays one out: as BLOCKS with paragraph spacing between them, not as a flat run of
    /// equally-spaced lines. One-variable step from <c>pdf-single-column-sv</c> — same content,
    /// same order, same single column; the only difference is that the vertical boundary between
    /// two employments exists in the geometry.
    ///
    /// <para>The corpus needed this case and did not have it. Its thirteen sibling PDF cases carry
    /// a single gap value end to end, so "zero blank lines" had two candidate causes — the
    /// extractor call suppressing them and the generator never authoring any — and the fixture set
    /// could only ever observe the pair. This case controls the second, which is what makes the
    /// first measurable.</para>
    /// </summary>
    internal static byte[] SingleColumnSpaced(CvModel m) =>
        Render(page => page.Content().Column(col =>
        {
            col.Spacing(WordDefaultParagraphSpacingPoints);
            SpacedBody(col, m);
        }));

    /// <summary>
    /// <see cref="SingleColumnSpaced"/> with the SAME paragraph spacing applied INSIDE each
    /// employment as well as between them, plus a long tightly-leaded skills list. One-variable
    /// step from <c>pdf-single-column-spaced</c>: the only change is that spacing now also falls
    /// between the lines of one entry.
    ///
    /// <para><b>This is the arm the corpus was missing, and its absence hid a regression</b>
    /// (#1060 PR E, measured 2026-07-27). Every other spaced case renders an employment as ONE
    /// block with spacing only between blocks, so no fixture could exhibit a paragraph gap INSIDE
    /// an entry. A geometry-derived boundary rule was built against that fixture set, passed every
    /// clause of its pre-committed acceptance rule, and was then measured on this shape to split
    /// entries apart — producing fragments with no organization, which
    /// <c>Resume.ValidateContent</c> rejects, turning a CV that promoted into a hard
    /// <c>IncompleteContent</c> block. The rule was withdrawn; this fixture is what makes the next
    /// attempt measurable instead of plausible.</para>
    ///
    /// <para><b>Why both knobs.</b> The intra-block spacing supplies the false-boundary candidate;
    /// the tight-leaded list supplies the population that keeps the page's MEDIAN gap at bare
    /// leading. Neither alone reproduces it — the failure is a window, not a threshold: with the
    /// list short the median rises to absorb the intra spacing, and with it very long the median
    /// falls back. Both are ordinary word-processor defaults (Normal's space-after; a list style
    /// that suppresses spacing between items of the same style).</para>
    /// </summary>
    internal static byte[] SingleColumnIntraBlockSpaced(CvModel m) =>
        Render(page => page.Content().Column(col =>
        {
            col.Spacing(WordDefaultParagraphSpacingPoints);

            Block(col, [m.PersonName, m.Email, m.Phone, m.City]);
            Block(col, [m.Headings.Profile]);
            Block(col, m.ProfileLines);

            // The tight-leaded list: no spacing between items, so these gaps hold the page median
            // down at bare leading. Emitted as ONE block, before the experience section.
            Block(col, [m.Headings.Skills]);
            Block(col, [.. m.Skills, .. m.Skills, .. m.Skills]);

            Block(col, [m.Headings.Experience]);
            foreach (var e in m.Employments)
            {
                // Each LINE of the employment is its own block, so Word's paragraph spacing falls
                // between them. That is the one variable this case adds.
                Block(col, [$"{e.Role} - {e.Marker}"]);
                Block(col, [e.Period]);
                Block(col, [e.Bullet]);
            }

            Block(col, [m.Headings.Education]);
            foreach (var e in m.Educations)
            {
                Block(col, [$"{e.Degree} - {e.Marker}"]);
                Block(col, [e.Period]);
            }

            Block(col, [m.Headings.Languages]);
            Block(col, m.Languages);
            Block(col, [m.Headings.UnknownProjects]);
            Block(col, m.ProjectLines);
        }));

    /// <summary>
    /// <see cref="SidebarEmittedFirst"/> with the same paragraph spacing as
    /// <see cref="SingleColumnSpaced"/> — a one-variable step from <c>pdf-sidebar-emitted-first</c>.
    ///
    /// <para>This case exists to record a LIMIT, not a fix. The two-column shape is the one PR K
    /// anchored to the real CV that filed #1060, and a reader who sees the spaced single-column
    /// case improve will reasonably assume the spaced sidebar does too. It does not: the sidebar's
    /// two columns share baselines with each other, so a geometry-derived boundary cannot be
    /// correlated to the extractor's column-sequential line order at all. Carrying that as a
    /// measured row rather than a sentence in a PR body is the whole point — prose is not
    /// regenerated when the code changes.</para>
    /// </summary>
    internal static byte[] SidebarSpaced(CvModel m) =>
        Render(page => page.Content().Row(row =>
        {
            row.ConstantItem(SidebarWidthPoints).Column(col =>
            {
                col.Spacing(WordDefaultParagraphSpacingPoints);
                Block(col, SidebarBlocks(m));
            });
            row.RelativeItem().PaddingLeft(20).Column(col =>
            {
                col.Spacing(WordDefaultParagraphSpacingPoints);
                SpacedBody(col, m, includeIdentity: false);
            });
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

    // ---- SPACED authoring (#1060 PR E) --------------------------------------------------
    // The spaced cases differ from their unspaced twins in exactly one thing: lines are grouped
    // into BLOCKS and the parent column spaces the blocks apart. Within a block the lines keep
    // ordinary leading, so the gap that appears between two employments is the only new signal.
    // These helpers are used ONLY by the spaced cases — every pre-existing case still renders
    // through the unspaced helpers below and its bytes are unchanged.

    /// <summary>One block: its lines at ordinary leading, spaced from its neighbours by the
    /// parent column. An empty block renders nothing (no stray gap).</summary>
    private static void Block(ColumnDescriptor col, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return;

        col.Item().Column(block =>
        {
            foreach (var line in lines)
                block.Item().Text(line);
        });
    }

    private static void Block(ColumnDescriptor col, IReadOnlyList<IReadOnlyList<string>> blocks)
    {
        foreach (var block in blocks)
            Block(col, block);
    }

    /// <summary>The spaced document body, in the same order <see cref="SingleColumn"/> emits it.
    /// Each employment and each education is its OWN block — that is the boundary the case is
    /// about. Headings are their own blocks, as a word processor's heading style produces.</summary>
    private static void SpacedBody(ColumnDescriptor col, CvModel m, bool includeIdentity = true)
    {
        if (includeIdentity)
            Block(col, [m.PersonName, m.Email, m.Phone, m.City]);

        Block(col, [m.Headings.Profile]);
        Block(col, m.ProfileLines);

        Block(col, [m.Headings.Experience]);
        foreach (var e in m.Employments)
            Block(col, [$"{e.Role} - {e.Marker}", e.Period, e.Bullet]);

        Block(col, [m.Headings.Education]);
        foreach (var e in m.Educations)
            Block(col, [$"{e.Degree} - {e.Marker}", e.Period]);

        Block(col, [m.Headings.Skills]);
        Block(col, m.Skills);
        Block(col, [m.Headings.Languages]);
        Block(col, m.Languages);
        Block(col, [m.Headings.UnknownProjects]);
        Block(col, m.ProjectLines);
    }

    /// <summary>The sidebar's blocks, mirroring <see cref="SidebarLines"/>'s content exactly so the
    /// spaced and unspaced sidebar cases differ only in spacing.</summary>
    private static List<IReadOnlyList<string>> SidebarBlocks(CvModel m) =>
    [
        [m.PersonName, m.Email, m.Phone, m.City],
        [m.Headings.Skills],
        m.Skills,
        [m.Headings.Languages],
        m.Languages,
    ];

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
