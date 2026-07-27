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
    /// shape a two-column Word template produces. No blank paragraphs. The cell order inverts the
    /// header line, which is what makes <c>SplitTitleOrganization</c> read Title as null.</summary>
    internal static byte[] TableLabelFirstNoBlanks(CvModel m) =>
        Build(m, useTable: true, blankSeparators: false, roleFirst: false);

    /// <summary>The invisibility probe: identical content and order, NO table at all. Paired with
    /// <see cref="TableLabelFirstNoBlanks"/>, whose extracted text this must equal.</summary>
    internal static byte[] FlatLabelFirstNoBlanks(CvModel m) =>
        Build(m, useTable: false, blankSeparators: false, roleFirst: false);

    /// <summary>The one-variable twin of <see cref="TableLabelFirstNoBlanks"/>: same body, only
    /// <c>&lt;w:p&gt;&lt;w:pPr /&gt;&lt;/w:p&gt;</c> separators added. This is the arm that isolates
    /// BLANK-LINE loss at the segmenter level (entries 5 vs 1) while holding header-line order
    /// fixed. Its promote outcome is expected to stay blocked, because the label-first header line
    /// still yields a null Title — which is precisely why it must not be conflated with the
    /// role-first arm.</summary>
    internal static byte[] TableLabelFirstWithBlanks(CvModel m) =>
        Build(m, useTable: true, blankSeparators: true, roleFirst: false);

    /// <summary>The PROMOTE-level control: blank separators AND role-first header lines. Measured
    /// 2026-07-26 to return 5/5 experiences and 3/3 educations with title, organization and period
    /// all populated. It exonerates the segmenter — same segmenter, same content, blank lines
    /// preserved, faithful result. It is a one-variable step from
    /// <see cref="TableLabelFirstWithBlanks"/> (header-line order), NOT from the first arm; a
    /// comparison against the first arm moves two variables and the report says so.</summary>
    internal static byte[] RoleFirstWithBlanks(CvModel m) =>
        Build(m, useTable: true, blankSeparators: true, roleFirst: true);

    private static byte[] Build(CvModel m, bool useTable, bool blankSeparators, bool roleFirst)
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
                var header = roleFirst ? $"{e.Role} - {e.Marker}" : e.Period;
                var second = roleFirst ? e.Period : $"{e.Role} - {e.Marker}";

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

            Line(m.Headings.Education);
            Blank();
            foreach (var e in m.Educations)
            {
                Line(roleFirst ? $"{e.Degree} - {e.Marker}" : e.Period);
                Line(roleFirst ? e.Period : $"{e.Degree} - {e.Marker}");
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
