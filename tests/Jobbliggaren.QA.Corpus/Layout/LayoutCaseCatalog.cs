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

    /// <summary>The project heading this case ACTUALLY renders, or null if it renders none.
    /// Pin P7 and the contamination sweep both measure against this rather than against a heading
    /// constant: an earlier revision measured every case against the UNKNOWN heading, so the
    /// paired control — which renders the KNOWN one — read "no" unconditionally and could not
    /// fall, and the contamination sweep reported a finding for a heading the document never
    /// contained.</summary>
    string? ProjectHeadingRendered = null,

    /// <summary>The account holder's display name this case registers. It is a case input because
    /// the auto-promote handler feeds it into the composed DTO, making it the only text the DQ6
    /// guard sees that the import scan did not already cover.</summary>
    string AccountDisplayName = LayoutCaseCatalog.DefaultAccountName);

/// <summary>The authored cases, ordered PDF then DOCX with controls adjacent to what they
/// control. The count is deliberately NOT written here: two revisions of this comment carried a
/// stale number (16 while 17 existed, then 19 while 20 existed), and a third revision replaced
/// them with a claim about where the count is emitted that was itself false. Count the list.</summary>
public static class LayoutCaseCatalog
{
    private const string Pdf = "application/pdf";
    private const string Docx =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>The account name every case registers unless it is specifically probing the DQ6
    /// guard. Deliberately not a person's real-looking name and never a personnummer.</summary>
    internal const string DefaultAccountName = "Konto Kontosson";

    /// <summary>The synthetic Luhn-valid personnummer, taken from the corpus's OWN lexicon rather
    /// than re-declared here. That list is what every existing PII-leak sweep in this project
    /// enumerates (<c>GeneratorDeterminismTests</c>, <c>CvReviewCorpusStressTests</c>,
    /// <c>CvImprovementCorpusStressTests</c>); a private copy would sit outside every sweep written
    /// against it, now and in future. One home per value.</summary>
    private static readonly string SyntheticPersonnummer =
        Jobbliggaren.QA.Corpus.Generation.SwedishCorpusLexicon.FakePersonnummer[0];

    // The sidebar is 150 pt wide inside a 40 pt margin, so the column boundary sits near x = 200.
    private const double ColumnSplitX = 200;

    private static readonly string UnknownProjectHeading =
        CvModel.Swedish.Headings.UnknownProjects;

    private static readonly string KnownProjectHeading = CvModel.Swedish.Headings.KnownProjects;

    private static readonly CvModel PnrModel =
        CvModel.Swedish with { SyntheticPersonnummer = SyntheticPersonnummer };

    public static IReadOnlyList<LayoutCase> All { get; } =
    [
        new("pdf-sidebar-emitted-first",
            "two geometric columns, emitted column-sequentially (sidebar block before main block)",
            "(a) two-column/sidebar — answered",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SidebarEmittedFirst, CvModel.Swedish,
            p =>
            {
                p.RequireVerticalGutter(15);
                // #1060 PR E: the UNSPACED half of the pair with pdf-sidebar-spaced, per column
                // (see RequireAuthoredParagraphSpacing on why a merged baseline series lies here).
                p.RequireUniformLineSpacing(tolerancePoints: 2, splitX: ColumnSplitX);
            },
            "a vertical gutter of at least 15 pt exists (a single-column render cannot produce it), "
            + "AND within each column every inter-baseline gap is within 2 pt of every other "
            + "(uniform leading — no authored block spacing)",
            SpikeMeasuredExtractSegment: true,
            ProjectHeadingRendered: UnknownProjectHeading),

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
            p =>
            {
                p.RequireNoVerticalGutter(15);
                // #1060 PR E: the UNSPACED half of the one-variable pair with
                // pdf-single-column-spaced. Declared, not assumed: without this the claim "this
                // document authors no paragraph spacing" was a property of the renderer that no
                // artifact recorded, which is exactly how the corpus came to be blind to spacing
                // in the first place. It is the exact negation of the spaced twin's proof, so the
                // two cannot both hold.
                p.RequireUniformLineSpacing(tolerancePoints: 2);
            },
            "no vertical gutter of 15 pt or more exists (not multi-column), AND every inter-baseline "
            + "gap is within 2 pt of every other (uniform leading — no authored block spacing)",
            SpikeMeasuredExtractSegment: true,
            ProjectHeadingRendered: UnknownProjectHeading),

        // #1060 PR E — the SPACED cases. Every case above authors uniform leading, which made the
        // corpus unable to separate "the extractor discards the paragraph boundary" from "the
        // document never carried one". These supply the missing control. A geometry-derived
        // boundary rule was built against them and WITHDRAWN when a later arm measured it turning
        // a promoting CV into a hard block, so nothing in the tree reads them today. They exist so
        // the NEXT attempt is measured rather than plausible (CTO-bind 2026-07-27 §A1.1 +
        // amendment 2).
        new("pdf-single-column-spaced",
            "single-column chronological, authored as blocks with paragraph spacing between them "
            + "(the way a word processor lays a CV out)",
            "(b) single-column chronological — the SPACED arm, which the class was missing",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SingleColumnSpaced, CvModel.Swedish,
            p =>
            {
                p.RequireNoVerticalGutter(15);
                p.RequireAuthoredParagraphSpacing(minCount: 8, minExtraPoints: 6);
            },
            "no vertical gutter of 15 pt or more exists (still one column), AND at least eight "
            + "inter-baseline gaps exceed the tightest by 6 pt or more (the authored block spacing)",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "pdf-single-column-sv",
            ProjectHeadingRendered: UnknownProjectHeading),

        new("pdf-single-column-intra-block-spaced",
            "single-column chronological, paragraph spacing between AND inside employments",
            "(b) single-column chronological — the arm that exhibits an intra-ENTRY paragraph gap",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SingleColumnIntraBlockSpaced, CvModel.Swedish,
            p =>
            {
                p.RequireNoVerticalGutter(15);
                p.RequireAuthoredParagraphSpacing(minCount: 8, minExtraPoints: 6);
                // The distinguishing claim, PROVED rather than authored: a gap sits between the
                // first employment's period line and its description line — INSIDE one entry.
                // Without this the case would differ from pdf-single-column-spaced only in the
                // renderer, and no artifact would record it.
                p.RequireGapBetweenLines("2026", "Ansvarig", minExtraPoints: 6);
            },
            "no vertical gutter of 15 pt or more exists (still one column), at least eight "
            + "inter-baseline gaps exceed the tightest by 6 pt or more, AND one of those gaps falls "
            + "INSIDE an employment (between its period line and its description line) — the "
            + "distinction no other case can make",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "pdf-single-column-spaced",
            ProjectHeadingRendered: UnknownProjectHeading),

        new("pdf-single-column-intra-block-spaced-tight-list",
            "the same, with the skills list lengthened so bare leading is the page's MEDIAN gap",
            "(b) single-column chronological — the second knob, isolated from the first",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SingleColumnIntraBlockSpacedTightList,
            CvModel.Swedish,
            p =>
            {
                p.RequireNoVerticalGutter(15);
                p.RequireAuthoredParagraphSpacing(minCount: 8, minExtraPoints: 6);
                p.RequireGapBetweenLines("2026", "Ansvarig", minExtraPoints: 6);
            },
            "the same form as its predecessor, with a longer tightly-leaded list — so the two "
            + "knobs the withdrawn boundary rule failed on are separated into two measured rows "
            + "instead of asserted together in prose",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "pdf-single-column-intra-block-spaced",
            ProjectHeadingRendered: UnknownProjectHeading),

        new("pdf-sidebar-spaced",
            "two geometric columns emitted column-sequentially, WITH paragraph spacing between "
            + "blocks — the shape the real CV in #1060 was measured to have",
            "(a) two-column/sidebar — the SPACED arm; carries a LIMIT, not a fix",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SidebarSpaced, CvModel.Swedish,
            p =>
            {
                p.RequireVerticalGutter(15);
                // splitX separates the sidebar (page margin 40 + width 150 = ends at 190) from the
                // main column (starts at 210). Passing it is REQUIRED here: merged into one
                // baseline series the two columns' interleaving makes the "tightest gap" an
                // inter-column offset rather than a line pitch, and the proof would hold on a
                // document carrying no block spacing at all — silently certifying this case's
                // whole claim.
                p.RequireAuthoredParagraphSpacing(minCount: 8, minExtraPoints: 6, splitX: ColumnSplitX);
            },
            "a vertical gutter of at least 15 pt exists (still two columns), AND at least eight "
            + "inter-baseline gaps exceed the tightest by 6 pt or more — so any failure to recover "
            + "entries here is NOT the document withholding the boundary",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "pdf-sidebar-emitted-first",
            ProjectHeadingRendered: UnknownProjectHeading),

        new("pdf-single-column-en",
            "single-column chronological, English heading vocabulary (same renderer, same order)",
            "(e) English headings — answered as a RECOGNITION class, not a layout class",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SingleColumn, CvModel.English,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: true,
            ProjectHeadingRendered: CvModel.English.Headings.UnknownProjects),

        new("pdf-nonsequential-decorative",
            "decorative layered page: watermark text in the stream, identity block emitted LAST "
            + "while positioned at the page top",
            "(d) Canva-style — answered PARTIALLY, as a mechanic; no vendor export was measured",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.NonSequentialDecorative, CvModel.Swedish,
            p => p.RequireWordNearPageTop("Andersson", 0.25),
            "the identity block sits in the top quarter of the text area although it is emitted last",
            SpikeMeasuredExtractSegment: true,
            ProjectHeadingRendered: UnknownProjectHeading),

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
            SpikeMeasuredExtractSegment: true,
            ProjectHeadingRendered: UnknownProjectHeading),

        new("pdf-known-heading-after-profile",
            "the same slot with the KNOWN synonym — the paired control for P7",
            "pin P7 control",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.KnownHeadingAfterProfile, CvModel.Swedish,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: true,
            OneVariableStepFrom: "pdf-unknown-heading-after-profile",
            ProjectHeadingRendered: KnownProjectHeading),

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
            "gate axis — a personnummer in the CV BODY, which blocks at the parse-level rung",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.PersonnummerBearing, PnrModel,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: false),

        // The DQ6 rung's ONLY route. A body-borne personnummer is pre-empted by the parse-level
        // gate, so the case above can never reach it; without this one, deleting the DQ6 guard
        // call would leave the entire report byte-identical -- which is precisely the regression
        // the personnummer cases exist to catch. The account display name is the one text the
        // composed DTO adds over the import-scanned superset, and the handler says so itself.
        //
        // Since #1117 this name is a LEGACY-ROW state, not a registrable one: JobSeeker.Register
        // refuses a personnummer-shaped display name, so the probe writes the column directly
        // (CvChainProbe names the actor). The rung it exercises is unchanged -- DQ6 is kept
        // precisely because rows written before that invariant still exist -- so this case now
        // measures the guard over the population that can still reach it.
        new("pdf-clean-body-pnr-in-account-name",
            "a CLEAN CV body whose ACCOUNT display name carries a synthetic personnummer",
            "gate axis — the only route to the DQ6 rung on the composed DTO",
            "pdf", "cv.pdf", Pdf, QuestPdfCvRenderer.SingleColumn, CvModel.Swedish,
            p => p.RequireNoVerticalGutter(15),
            "no vertical gutter of 15 pt or more exists",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "pdf-single-column-sv",
            ProjectHeadingRendered: UnknownProjectHeading,
            AccountDisplayName: "Konto Kontosson " + SyntheticPersonnummer),

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
            SpikeMeasuredExtractSegment: true,
            ProjectHeadingRendered: UnknownProjectHeading),

        new("docx-flat-label-first-no-blanks",
            "identical content and order with NO table — the table-invisibility probe",
            "(c) table-based Word template — the twin that proves table-ness is invisible",
            "docx", "cv.docx", Docx, OpenXmlCvRenderer.FlatLabelFirstNoBlanks, CvModel.Swedish,
            p => p.Require(!p.DocxDocumentXml().Contains("<w:tbl>", StringComparison.Ordinal),
                "expected NO w:tbl element"),
            "the package contains no w:tbl",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "docx-table-label-first-no-blanks",
            ProjectHeadingRendered: UnknownProjectHeading),

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
            OneVariableStepFrom: "docx-table-label-first-no-blanks",
            ProjectHeadingRendered: UnknownProjectHeading),

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
            OneVariableStepFrom: "docx-table-label-first-with-blanks",
            ProjectHeadingRendered: UnknownProjectHeading),

        // The fourth cell of the useTable 2x2 (header order x blank separators). Every no-blanks
        // arm the corpus shipped was ALSO label-first, so "1 of 5 entries" and "null Title" were
        // confounded and no row could tell them apart. This arm holds the header order right and
        // removes the blank paragraphs, which separates them.
        new("docx-role-first-no-blanks",
            "role-first header lines with NO blank paragraphs — separates entry-boundary loss from header order",
            "(c) table-based Word template — the control that de-confounds the two no-blanks variables",
            "docx", "cv.docx", Docx, OpenXmlCvRenderer.RoleFirstNoBlanks, CvModel.Swedish,
            p =>
            {
                var xml = p.DocxDocumentXml();
                p.Require(xml.Contains("<w:tbl>", StringComparison.Ordinal), "expected a w:tbl element");
                p.Require(!xml.Contains("<w:pPr />", StringComparison.Ordinal)
                          && !xml.Contains("<w:pPr/>", StringComparison.Ordinal),
                    "found Word's blank-paragraph form — this arm authors NO blank separators, and "
                    + "a blank line here would make it a duplicate of docx-role-first-with-blanks");
                p.Require(!xml.Contains("<w:p />", StringComparison.Ordinal)
                          && !xml.Contains("<w:p/>", StringComparison.Ordinal),
                    "expected no self-closing w:p (this arm authors no blank paragraphs)");
            },
            "the package contains a w:tbl, no blank-paragraph <w:pPr /> and no self-closing <w:p />",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "docx-role-first-with-blanks",
            ProjectHeadingRendered: UnknownProjectHeading),

        // #1060 beta-1's COST arm. Every other arm writes its field-bearing line role-before-
        // marker, so the shape where the two slots come out SWAPPED was invisible to every row.
        // beta-1 moved that population from an honest block to a promote, and an accepted cost that
        // is published nowhere is a laundered one. One variable from row 20: within-line order.
        new("docx-company-first-header",
            "the field-bearing line written COMPANY-first — the shape whose slots come out swapped",
            "(c) table-based Word template — the arm that publishes beta-1's cost",
            "docx", "cv.docx", Docx, OpenXmlCvRenderer.CompanyFirstHeaderWithBlanks, CvModel.Swedish,
            p =>
            {
                var xml = p.DocxDocumentXml();
                p.Require(xml.Contains("<w:tbl>", StringComparison.Ordinal), "expected a w:tbl element");
                // The arm IS the inverted order, so the proof asserts the order itself rather than
                // the container. Without this the renderer could silently emit role-first and the
                // row would publish a reassuring verdict about a document it never rendered.
                p.Require(
                    xml.Contains("Klarna AB - Senior backend-utvecklare", StringComparison.Ordinal),
                    "expected the employment line written COMPANY-first");
                p.Require(
                    !xml.Contains("Senior backend-utvecklare - Klarna AB", StringComparison.Ordinal),
                    "found the role-first form — this arm would then duplicate docx-table-label-first-with-blanks");
                p.Require(
                    xml.Contains("Chalmers tekniska högskola - Civilingenjör", StringComparison.Ordinal),
                    "expected the education line written INSTITUTION-first (both parsers share one split)");
                // Pin the variable this arm HOLDS FIXED, not only the one it moves. It is a
                // one-variable step from docx-table-label-first-with-blanks, and that claim is
                // false the moment the blank separators go missing — the row would then differ on
                // two axes and its verdict would be unattributable.
                p.Require(xml.Contains("<w:pPr />", StringComparison.Ordinal)
                          || xml.Contains("<w:pPr/>", StringComparison.Ordinal),
                    "expected Word's blank-paragraph form — this arm HOLDS blank separators fixed");
            },
            "the employment and education lines are written company/institution-first, in a w:tbl, "
            + "with the blank separators the one-variable step holds fixed",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "docx-table-label-first-with-blanks",
            ProjectHeadingRendered: UnknownProjectHeading),

        // #1060 D3(β-3). The corpus has never carried a block that is IRREDUCIBLY non-buildable:
        // every arm authors complete entries, and the three that once blocked did so because a
        // parser defect destroyed a field the document did carry (β-1 fixed it). This one is
        // different in kind — no upstream fix can BUILD this block, because the employer is
        // absent from the SOURCE.
        //
        // AND WHEN THE ARM WAS AUTHORED, THE PARSE DID NOT REFUSE IT. Measured, not predicted: it
        // was written expecting `Blocked` on Resume.ExperienceCompanyRequired, and it published
        // PromotedInflated instead. SplitTitleOrganization's fallback took Lines[1] as the
        // organization whenever Lines[0] carried no separator glyph — and on a block with no
        // employer, Lines[1] IS the period line. So the CV promoted with "2026 - 2026" as the
        // employer name: the engine did not drop a field, it ASSERTED one the source never made.
        // The fallback was original code, byte-identical since before β-1; it had no reader until
        // now because every other arm authors an employer, so Lines[0] always carries a separator.
        //
        // The refutation is recorded HERE rather than pointed at by commit, because this PR
        // squash-merges and no intermediate commit survives on main to point at. #1060 β-3 then
        // narrowed the fallback so a field-less line cannot BECOME a field, and this row reaches
        // the refusal it was authored to measure.
        //
        // The segmenter behaviour is pinned by HeadingDrivenResumeSegmenterTests'
        // Segment_HeaderLineCarryingNoSeparator_* family — named here because this corpus is
        // observe-only and cannot fail on a regression by itself: its report is written to a
        // gitignored artifact and no test compares it to the committed baseline.
        //
        // It steps from the FAITHFUL control, not a lossy arm: stepping from a lossy one would
        // confound "the Domain refused this entry" with "the parser already lost that entry".
        new("docx-irreducible-unattributed-experience",
            "the clean promote control plus ONE experience block that names no employer",
            "(c) table-based Word template — the only arm whose block no upstream fix can build",
            "docx", "cv.docx", Docx,
            OpenXmlCvRenderer.RoleFirstWithBlanksAndUnattributedBlock, CvModel.SwedishWithUnattributedExperience,
            p =>
            {
                var xml = p.DocxDocumentXml();
                // The moved variable: the block IS in the document, in the arm's own header shape.
                p.Require(
                    xml.Contains("Frilansande systemutvecklare", StringComparison.Ordinal),
                    "expected the employer-less experience block to be rendered");
                // And its role line carries NO EMPLOYER. Stated as what this actually proves: the
                // renderer writes u.Role in its own w:t node (employments join as
                // $"{Role} - {Marker}" in ONE node), so the only way a separator can reach this
                // XML beside the freelance role is from inside the Role LITERAL. That is the
                // mutation this proof exists to catch — a fixture edit fusing an employer into the
                // role would turn the arm from irreducible into fused, and row 24 would leave
                // Blocked while §0 still reported the instrument healthy.
                //
                // ALL NINE separators, not just " - ": SplitTitleOrganization tries " — " and
                // " – " BEFORE the hyphen, so covering only the hyphen would miss the two that
                // decide first. The table is duplicated here because the fixture cannot read the
                // segmenter's internal constant — that is a knowingly accepted copy of a lexicon,
                // not the corpus measuring its own constants: the copy is checked against
                // AUTHORED BYTES, and if it drifts the arm still fails loudly on the split.
                foreach (var separator in new[]
                         { " — ", " – ", " - ", ", ", " | ", " @ ", " at ", " på ", " hos " })
                {
                    p.Require(
                        !xml.Contains("Frilansande systemutvecklare" + separator, StringComparison.Ordinal),
                        $"found the separator '{separator}' after the freelance role — this arm's "
                        + "block must carry no employer at all, or it is a fused entry rather than "
                        + "an irreducible one");
                }
                // Variables HELD FIXED, so the one-variable claim is checkable rather than
                // asserted: role-first header order, a w:tbl, and Word's blank-paragraph form.
                p.Require(xml.Contains("<w:tbl>", StringComparison.Ordinal), "expected a w:tbl element");
                p.Require(
                    xml.Contains("Senior backend-utvecklare - Klarna AB", StringComparison.Ordinal),
                    "expected the employment lines role-first, as the control renders them");
                p.Require(xml.Contains("<w:pPr />", StringComparison.Ordinal)
                          || xml.Contains("<w:pPr/>", StringComparison.Ordinal),
                    "expected Word's blank-paragraph form — the step holds blank separators fixed");
            },
            "the employer-less block is present with no separator after its role, in a w:tbl, with "
            + "role-first employment lines and the blank separators the one-variable step holds fixed",
            SpikeMeasuredExtractSegment: false,
            OneVariableStepFrom: "docx-role-first-with-blanks",
            ProjectHeadingRendered: UnknownProjectHeading),
    ];

    /// <summary>
    /// Pin P5 rests on the English model being structurally comparable to the Swedish one. This
    /// checks CARDINALITIES only — section set and section ORDER are guaranteed structurally
    /// instead, because both cases run the SAME renderer method
    /// (<c>QuestPdfCvRenderer.SingleColumn</c>) and a renderer emits its sections in one order.
    /// Stated precisely because a guard whose comment promises more than its code checks is a
    /// defect class this repo has already paid for.
    /// </summary>
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
