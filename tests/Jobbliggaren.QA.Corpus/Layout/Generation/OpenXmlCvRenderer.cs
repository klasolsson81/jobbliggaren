using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlDocument = DocumentFormat.OpenXml.Wordprocessing.Document;

namespace Jobbliggaren.QA.Corpus.Layout.Generation;

/// <summary>
/// Renders REAL DOCX bytes, one method per arm. DOCX is the corpus's CONTROL container: it is the
/// only one that can carry a blank line end-to-end, which is what lets the report separate "the
/// segmenter is broken" from "the PDF container carries no entry boundary".
///
/// <para><b>Two container facts, both measured, both load-bearing.</b></para>
///
/// <para><b>1. A blank paragraph only exists in one authoring form.</b>
/// <c>new Paragraph()</c> serialises to the SELF-CLOSING <c>&lt;w:p /&gt;</c>. <c>XmlReader</c>
/// raises no <c>EndElement</c> for a self-closing element, so
/// <c>PdfPigOpenXmlCvTextExtractor</c>'s <c>EndElement when LocalName == "p"</c> newline handler
/// never fires and the blank line DOES NOT EXIST. <c>new Paragraph(new ParagraphProperties())</c>
/// serialises to <c>&lt;w:p&gt;&lt;w:pPr /&gt;&lt;/w:p&gt;</c> and does produce it. Word writes the
/// latter. Measured 2026-07-26: two documents differing only in which form they used produced
/// BYTE-IDENTICAL extracted text at 1393 chars. A fixture that builds blank lines the first way
/// silently measures a fiction, which is why <c>ByteProof</c> asserts the serialization form from
/// <c>word/document.xml</c> rather than trusting this comment.</para>
///
/// <para><b>2. A Word table is BYTE-INVISIBLE to the extractor.</b> The DOCX branch handles
/// exactly three node conditions — <c>w:t</c> Element, <c>w:t</c> EndElement, and <c>w:p</c>
/// EndElement. There is no handling of <c>w:tbl</c>, <c>w:tr</c>, <c>w:tc</c> or <c>w:br</c>. A
/// table and a flat paragraph sequence in the same order therefore produce identical text. So
/// "table-based Word template" cannot be a DEFINING mechanic here, and an ordering assertion over
/// the extracted text would restate this file rather than measure the product. The corpus covers
/// the class by shipping the invisibility itself as a measurement (the flat twin).</para>
/// </summary>
internal static class OpenXmlCvRenderer
{
    /// <summary>A Word table, one row per job, PERIOD cell before ROLE cell — the label-first
    /// shape a two-column Word template produces. No blank paragraphs.
    /// <para>UNTIL #1060 β-1 the cell order made <c>SplitTitleOrganization</c> read Title as null,
    /// because the split ran against a line carrying nothing but a period. It now reads the next
    /// line instead, so this arm parses its one fused entry and PROMOTES (lossily — it still yields
    /// one entry of five, for the unrelated reason that the document authors no blank paragraphs
    /// and <c>SplitEntries</c> splits on those alone).</para></summary>
    internal static byte[] TableLabelFirstNoBlanks(CvModel m) =>
        Build(m, useTable: true, blankSeparators: false, roleFirst: false, companyFirst: false);

    /// <summary>The invisibility probe: identical content and order, NO table at all. Paired with
    /// <see cref="TableLabelFirstNoBlanks"/>, whose extracted text this must equal.</summary>
    internal static byte[] FlatLabelFirstNoBlanks(CvModel m) =>
        Build(m, useTable: false, blankSeparators: false, roleFirst: false, companyFirst: false);

    /// <summary>The one-variable twin of <see cref="TableLabelFirstNoBlanks"/>: same body, only
    /// <c>&lt;w:p&gt;&lt;w:pPr /&gt;&lt;/w:p&gt;</c> separators added. This is the arm that isolates
    /// BLANK-LINE loss at the segmenter level (entries 5 vs 1) while holding header-line order
    /// fixed.
    /// <para>Its promote outcome WAS expected to stay blocked, on the reasoning that a label-first
    /// header line still yields a null Title. #1060 β-1 removed that cause, and this arm now promotes
    /// FAITHFULLY — 5/5, 3/3, eight markers Survived. Count the verdicts in §2 rather than
    /// trusting an ordinal here; one was written and went stale inside a single PR. It remains
    /// the blank-line isolator it was authored to be; only its verdict moved.</para></summary>
    internal static byte[] TableLabelFirstWithBlanks(CvModel m) =>
        Build(m, useTable: true, blankSeparators: true, roleFirst: false, companyFirst: false);

    /// <summary>The PROMOTE-level control: blank separators AND role-first header lines. Measured
    /// 2026-07-26 to return 5/5 experiences and 3/3 educations with title, organization and period
    /// all populated. It exonerates the segmenter — same segmenter, same content, blank lines
    /// preserved, faithful result. It is a one-variable step from
    /// <see cref="TableLabelFirstWithBlanks"/> (header-line order), NOT from the first arm; a
    /// comparison against the first arm moves two variables and the report says so.</summary>
    internal static byte[] RoleFirstWithBlanks(CvModel m) =>
        Build(m, useTable: true, blankSeparators: true, roleFirst: true, companyFirst: false);

    /// <summary>The fourth cell of the <c>useTable: true</c> 2×2 over (header order × blank
    /// separators), which the other three above already occupy. It exists to answer ONE question
    /// the corpus could not answer before: is the entry-boundary loss on the no-blanks arms a
    /// property of the DOCUMENT (no blank paragraph ⇒ <c>SplitEntries</c> cannot split) or a
    /// property of the label-first HEADER ORDER? Every no-blanks arm the corpus shipped was also
    /// label-first, so the two variables were confounded and the report could not separate them.
    ///
    /// <para>It is a one-variable step from <see cref="RoleFirstWithBlanks"/> (blank separators
    /// removed) and, equally, from <see cref="TableLabelFirstNoBlanks"/> (header order inverted).
    /// The record carries one <c>OneVariableStepFrom</c>, so the catalog declares the first; the
    /// second pairing is stated here because it is what makes this arm a CONTROL rather than a
    /// twenty-second measurement.</para></summary>
    internal static byte[] RoleFirstNoBlanks(CvModel m) =>
        Build(m, useTable: true, blankSeparators: false, roleFirst: true, companyFirst: false);

    /// <summary>The COST arm for #1060 β-1, and the only arm in the corpus that authors its
    /// field-bearing line COMPANY-first (<c>"Klarna AB - Senior backend-utvecklare"</c>). A
    /// one-variable step from <see cref="TableLabelFirstWithBlanks"/>: same container, same blank
    /// separators, same period-first cell order — only the within-line order inverted.
    ///
    /// <para>It exists because β-1 moved a population from an honest block to a promote whose two
    /// slots are SWAPPED, and that cost was published nowhere: every other arm writes
    /// role-before-marker, so no existing row could see the shape. A cost accepted and
    /// unpublished is a cost laundered.</para>
    ///
    /// <para>It is also where the INSTRUMENT gets measured rather than the parser.
    /// <c>LayoutChainRunner.Decide</c> reads entry COUNTS, so this row prints
    /// <c>PromotedFaithful</c>; <c>MarkerTrace</c> reads structure, so every marker prints
    /// <c>RetainedButOrphaned</c>. It is the first row where the two verdicts disagree in the
    /// direction "the numbers are right and the content is wrong".</para></summary>
    internal static byte[] CompanyFirstHeaderWithBlanks(CvModel m) =>
        Build(m, useTable: true, blankSeparators: true, roleFirst: false, companyFirst: true);

    /// <summary>The IRREDUCIBLE arm (#1060 D3(β-3)): every axis held at
    /// <see cref="RoleFirstWithBlanks"/>'s coordinate — the corpus's clean promote control, 5/5
    /// and 3/3 — with ONE variable moved, an extra experience block that names a role and a
    /// period and NO employer.
    /// <para>Its subject is a document the engine parses CORRECTLY. Nothing is fused, nothing is
    /// mis-slotted, no blank line is missing: the employer is absent because the SOURCE has none,
    /// which is a normal thing for a CV to say. That is what makes it the only arm in the corpus
    /// whose block is irreducibly non-buildable, and why the control it steps from must be the
    /// faithful one — stepping from a lossy arm would confound "the Domain refused this entry"
    /// with "the parser already lost that entry".</para>
    /// <para>The arm takes NO position on routing. It measures HEAD, so that whatever a router
    /// would later do to this block has a base to be measured against (R4: measure the base, then
    /// the delta).</para></summary>
    internal static byte[] RoleFirstWithBlanksAndUnattributedBlock(CvModel m) =>
        Build(m, useTable: true, blankSeparators: true, roleFirst: true, companyFirst: false);

    // NO DEFAULT on any axis, deliberately. This file is a factorial design and its whole
    // epistemic value is the one-variable step, so every call site must state its full grid
    // coordinate. The five sites that would have inherited `companyFirst: false` are exactly
    // the CONTROLS for the company-first arm — a default would hide the coordinate precisely
    // where it is load-bearing. Revisit an options/shape type at a FIFTH axis; four named
    // booleans still read.
    private static byte[] Build(
        CvModel m, bool useTable, bool blankSeparators, bool roleFirst, bool companyFirst)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
            stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Body();

            void Line(string text) => body.AppendChild(new Paragraph(new Run(new Text(text))));

            // The ONLY authoring form that produces a blank line — see the class remarks.
            void Blank()
            {
                if (blankSeparators)
                    body.AppendChild(new Paragraph(new ParagraphProperties()));
            }

            Line(m.PersonName);
            Line(m.Email);
            Line(m.Phone);
            Line(m.City);
            Blank();

            Line(m.Headings.Profile);
            foreach (var line in m.ProfileLines)
                Line(line);
            Blank();

            Line(m.Headings.Experience);
            Blank();
            foreach (var e in m.Employments)
            {
                var pair = companyFirst ? $"{e.Marker} - {e.Role}" : $"{e.Role} - {e.Marker}";
                var header = roleFirst ? pair : e.Period;
                var second = roleFirst ? e.Period : pair;

                if (useTable)
                {
                    body.AppendChild(new Table(new TableRow(
                        new TableCell(new Paragraph(new Run(new Text(header)))),
                        new TableCell(
                            new Paragraph(new Run(new Text(second))),
                            new Paragraph(new Run(new Text(e.Bullet)))))));
                }
                else
                {
                    Line(header);
                    Line(second);
                    Line(e.Bullet);
                }

                Blank();
            }

            // #1060 D3(β-3). Empty on every arm but the irreducible one, so this loop is a no-op
            // for the rest of the corpus. Rendered LAST inside the experience section and in the
            // arm's own header shape, so the block differs from its neighbours on exactly one
            // axis: it names no employer. Writing it in a different header order would move two
            // variables and make the row's verdict unattributable.
            foreach (var u in m.UnattributedExperience)
            {
                var header = roleFirst ? u.Role : u.Period;
                var second = roleFirst ? u.Period : u.Role;

                if (useTable)
                {
                    body.AppendChild(new Table(new TableRow(
                        new TableCell(new Paragraph(new Run(new Text(header)))),
                        new TableCell(
                            new Paragraph(new Run(new Text(second))),
                            new Paragraph(new Run(new Text(u.Bullet)))))));
                }
                else
                {
                    Line(header);
                    Line(second);
                    Line(u.Bullet);
                }

                Blank();
            }

            Line(m.Headings.Education);
            Blank();
            foreach (var e in m.Educations)
            {
                var eduPair = companyFirst ? $"{e.Marker} - {e.Degree}" : $"{e.Degree} - {e.Marker}";
                Line(roleFirst ? eduPair : e.Period);
                Line(roleFirst ? e.Period : eduPair);
                Blank();
            }

            Line(m.Headings.Skills);
            foreach (var s in m.Skills)
                Line(s);
            Blank();

            Line(m.Headings.Languages);
            foreach (var l in m.Languages)
                Line(l);
            Blank();

            Line(m.Headings.UnknownProjects);
            foreach (var p in m.ProjectLines)
                Line(p);

            mainPart.Document = new OpenXmlDocument(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
