using Jobbliggaren.QA.Corpus.Layout.Generation;

namespace Jobbliggaren.QA.Corpus.Layout;

/// <summary>
/// One authored CV document and everything the report needs to describe it HONESTLY.
///
/// <para><see cref="Mechanic"/> is the name that appears in the report, and it names a MECHANISM,
/// never a vendor. No genuine Canva, Word, InDesign, LaTeX or Europass export was run through the
/// extractor in any of this work, so a vendor-named case would assert something nobody measured.
/// <see cref="CtoClass"/> records which of the six CTO-named classes the case answers, and whether
/// it answers it fully or partially.</para>
///
/// <para><see cref="OneVariableStepFrom"/> is printed so no reader can mistake a two-variable
/// comparison for an isolation: the DOCX arms form a chain, and comparing the first to the last
/// moves both the blank-paragraph serialization AND the header-line order.</para>
///
/// <para><see cref="SpikeMeasuredExtractSegment"/> is per-SECTION provenance. The 2026-07-26 spike
/// measured extraction and segmentation only — it produced no gate verdict, no promote boolean and
/// no delta on a promoted CV. So it can vouch for the extraction columns of some rows and for
/// nothing else, and the report says exactly that rather than implying whole-row provenance.</para>
/// </summary>
public sealed record LayoutCase(
    string Id,
    string Mechanic,
    string CtoClass,
    string Container,
    string FileName,
    string ContentType,
    Func<CvModel, byte[]> Render,
    CvModel Model,
    Action<ByteProofContext> ProveBytes,
    string ByteProofDescription,
    bool SpikeMeasuredExtractSegment,
    string? OneVariableStepFrom = null,
    bool CarriesPersonnummer = false);

/// <summary>The 16 authored cases. Ordered PDF then DOCX, controls adjacent to what they
/// control.</summary>
public static class LayoutCaseCatalog
{
    private const string Pdf = "application/pdf";
    private const string Docx =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>A synthetic Luhn-valid personnummer, parity with the value the auto-promote
    /// handler tests already use. It is authored so the personnummer gates are exercised
    /// POSITIVELY; it is never written to the report, a log, or the committed baseline.</summary>
    private const string SyntheticPersonnummer = "811218-9876";

    // The sidebar is 150 pt wide inside a 40 pt margin, so the column boundary sits near x = 200.
    private const double ColumnSplitX = 200;

    private static readonly CvModel PnrModel =
        CvModel.Swedish with { SyntheticPersonnummer = SyntheticPersonnummer };

    public static IReadOnlyList<LayoutCase> All { get; } =
    [
        new("pdf-sidebar-emitted-first",
            "two geometric columns, emitted column-sequentially (sidebar block before main block)",
            "(a) two-column/sidebar — answered",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SidebarEmittedFirst, CvModel.Swedish,
            p => p.RequireVerticalGutter(15),
            "a vertical gutter of at least 15 pt exists, which a single-column render cannot produce",
            SpikeMeasuredExtractSegment: true),

        new("pdf-interleaved-baseline-fusion",
            "row-interleaved two-column generator: sidebar and main cells share every baseline",
            "(a) two-column/sidebar — the only shape whose two-column-ness the extractor makes visible",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.InterleavedBaselineFusion, CvModel.Swedish,
            p => p.RequireSharedBaselines(ColumnSplitX, 8),
            "at least 8 baselines carry words from both columns",
            SpikeMeasuredExtractSegment: true),

        new("pdf-zero-xgap-concat",
            "right-aligned period cell abutting a left-aligned company cell, zero padding",
            "(a) two-column/sidebar — the negative-x-gap defect the CTO named as known-remaining",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.ZeroXGapConcat, CvModel.Swedish,
            p => p.RequireDigitLetterFusedWord(),
            "a word fuses two cells (a digit immediately followed by a letter)",
            SpikeMeasuredExtractSegment: true),

        new("pdf-single-column-sv",
            "single-column chronological, blocks in document order",
            "(b) single-column chronological — answered",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SingleColumn, CvModel.Swedish,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists, so the page is not multi-column",
            SpikeMeasuredExtractSegment: true),

        new("pdf-single-column-en",
            "single-column chronological, English heading vocabulary (same renderer, same order)",
            "(e) English headings — answered as a RECOGNITION class, not a layout class",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SingleColumn, CvModel.English,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: true),

        new("pdf-nonsequential-decorative",
            "decorative layered page: watermark text in the stream, identity block emitted LAST "
            + "while positioned at the page top",
            "(d) Canva-style — answered PARTIALLY, as a mechanic; no vendor export was measured",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.NonSequentialDecorative, CvModel.Swedish,
            p => p.RequireWordNearPageTop("Andersson", 0.25),
            "the identity block sits in the top quarter of the text area although it is emitted last",
            SpikeMeasuredExtractSegment: true),

        new("pdf-headingless",
            "no headings at all — the HONEST-FAILURE control",
            "(f) headingless — answered",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.Headingless, CvModel.Swedish,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: true),

        new("pdf-unknown-heading-after-profile",
            "a heading the lexicon does not know, placed IMMEDIATELY AFTER the profile block",
            "pin P7 — position is load-bearing",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.UnknownHeadingAfterProfile, CvModel.Swedish,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: true),

        new("pdf-known-heading-after-profile",
            "the same slot with the KNOWN synonym — the paired control for P7",
            "pin P7 control",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.KnownHeadingAfterProfile, CvModel.Swedish,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: true,
            OneVariableStepFrom: "pdf-unknown-heading-after-profile"),

        new("pdf-decorated-heading-glue",
            "a known heading defeated by decorative glue (a leading bullet)",
            "recognition axis — its falsifier is a SOURCE edit where P7's is a DATA edit",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.DecoratedHeadingGlue, CvModel.Swedish,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: false),

        new("pdf-two-page-seam",
            "a page break MID-EXPERIENCE — the only case touching the page-seam newline",
            "extraction axis — covers PdfPigOpenXmlCvTextExtractor.cs:118, half the cited defect",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.TwoPageSeam, CvModel.Swedish,
            p => p.Require(p.PdfPageCount() == 2, "expected exactly 2 physical pages"),
            "the document has exactly 2 physical pages",
            SpikeMeasuredExtractSegment: false),

        new("pdf-pnr-bearing",
            "single column carrying a synthetic personnummer in the contact block",
            "gate axis — without it the personnummer gates are exercised only NEGATIVELY",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.PersonnummerBearing, PnrModel,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: false,
            CarriesPersonnummer: true),

        new("docx-table-label-first-no-blanks",
            "Word table, period cell before role cell, no blank paragraphs",
            "(c) table-based Word template — answered as a CONTAINER fact; table-ness is invisible",
            "docx", "cv.docx", Docx, OpenXmlCvRenderer.TableLabelFirstNoBlanks, CvModel.Swedish,
            p =>
            {
                var xml = p.DocxDocumentXml();
                p.Require(xml.Contains("<w:tbl>", StringComparison.Ordinal), "expected a w:tbl element");
                p.Require(!xml.Contains("<w:p />", StringComparison.Ordinal)
                          && !xml.Contains("<w:p/>", StringComparison.Ordinal),
                    "expected no self-closing w:p (this arm authors no blank paragraphs)");
            },
            "the package contains a w:tbl and no self-closing w:p",
            SpikeMeasuredExtractSegment: true),

        new("docx-flat-label-first-no-blanks",
            "identical content and order with NO table — the table-invisibility probe",
            "(c) table-based Word template — the twin that proves table-ness is invisible",
            "docx", "cv.docx", Docx, OpenXmlCvRenderer.FlatLabelFirstNoBlanks, CvModel.Swedish,
            p => p.Require(!p.DocxDocumentXml().Contains("<w:tbl>", StringComparison.Ordinal),
                "expected NO w:tbl element"),
            "the package contains no w:tbl",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "docx-table-label-first-no-blanks"),

        new("docx-table-label-first-with-blanks",
            "the same table body with Word's own blank-paragraph form added — isolates BLANK LINES",
            "(c) table-based Word template — one-variable step",
            "docx", "cv.docx", Docx, OpenXmlCvRenderer.TableLabelFirstWithBlanks, CvModel.Swedish,
            p =>
            {
                var xml = p.DocxDocumentXml();
                p.Require(xml.Contains("<w:pPr />", StringComparison.Ordinal)
                          || xml.Contains("<w:pPr/>", StringComparison.Ordinal),
                    "expected Word's blank-paragraph form <w:p><w:pPr /></w:p>");
                p.Require(!xml.Contains("<w:p />", StringComparison.Ordinal)
                          && !xml.Contains("<w:p/>", StringComparison.Ordinal),
                    "found a SELF-CLOSING w:p, which raises no EndElement and therefore emits no "
                    + "newline — this arm would silently measure a fiction");
            },
            "blank paragraphs use Word's <w:p><w:pPr /></w:p> form, never the self-closing <w:p />",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "docx-table-label-first-no-blanks"),

        new("docx-role-first-with-blanks",
            "blank paragraphs AND role-first header lines — the PROMOTE-level control",
            "(c) table-based Word template — the arm that exonerates the segmenter",
            "docx", "cv.docx", Docx, OpenXmlCvRenderer.RoleFirstWithBlanks, CvModel.Swedish,
            p =>
            {
                var xml = p.DocxDocumentXml();
                p.Require(xml.Contains("<w:pPr />", StringComparison.Ordinal)
                          || xml.Contains("<w:pPr/>", StringComparison.Ordinal),
                    "expected Word's blank-paragraph form");
                p.Require(!xml.Contains("<w:p />", StringComparison.Ordinal)
                          && !xml.Contains("<w:p/>", StringComparison.Ordinal),
                    "found a self-closing w:p, which emits no newline");
            },
            "blank paragraphs use Word's <w:p><w:pPr /></w:p> form",
            SpikeMeasuredExtractSegment: true,
            OneVariableStepFrom: "docx-table-label-first-with-blanks"),
    ];

    /// <summary>Pin P5 rests on the English model being structurally identical to the Swedish one:
    /// same section set, same section ORDER, same cardinalities. If that drifts, P5 stops being a
    /// non-difference claim and becomes permanent noise, so the divergence is caught here as an
    /// instrument failure rather than read as a product finding.</summary>
    public static IReadOnlyList<string> ValidateModelSymmetry()
    {
        var sv = CvModel.Swedish;
        var en = CvModel.English;
        var problems = new List<string>();

        if (sv.GroundTruthEmployments != en.GroundTruthEmployments)
            problems.Add("employment counts differ between the Swedish and English models");
        if (sv.GroundTruthEducations != en.GroundTruthEducations)
            problems.Add("education counts differ between the Swedish and English models");
        if (sv.Skills.Count != en.Skills.Count)
            problems.Add("skill counts differ");
        if (sv.Languages.Count != en.Languages.Count)
            problems.Add("language counts differ");
        if (sv.ProfileLines.Count != en.ProfileLines.Count)
            problems.Add("profile line counts differ");
        if (sv.ProjectLines.Count != en.ProjectLines.Count)
            problems.Add("project line counts differ");

        return problems;
    }
}
