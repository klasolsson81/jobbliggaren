using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Shouldly;
// The #891 font-run + end-to-end D3 tests drive the REAL review engine over the analyzer's
// output; reuse the shared engine builders (real knowledge-bank assets, all-correct spell stub).
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;
// `Document` is ambiguous between QuestPDF and OpenXml (both used here — QuestPDF builds the PDF
// fixtures, OpenXml the DOCX fixture). Alias the QuestPDF one; OpenXml's `Document` stays bare
// in BuildDocx.
using QuestDocument = QuestPDF.Fluent.Document;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

// Fas 4b PR-6b (ADR 0093 §D4, NO AI/LLM) — PdfPigCvLayoutAnalyzer is the PDF-geometry port impl
// (PdfPig confined behind it; internal, visible via InternalsVisibleTo). Unlike the text
// extractor's fail-soft-only PDF coverage, the geometry MUST be tested against REAL PDF bytes
// with a KNOWN margin — so these tests synthesize genuine PDFs via QuestPDF (the repo's own
// renderer). Contract under test:
//   • a text-bearing PDF → Analyzed with the true page count + the tightest page-edge margin;
//   • the margin MATH is load-bearing: a <1 cm margin reads below the 28.35 pt floor, a wide
//     margin reads above it (this is what E2 keys off);
//   • a DOCX → NotApplicable (no server-side conversion, D10) with the file size still known;
//   • empty / garbage / non-PDF bytes as Pdf → Failed, NEVER throws, file size still known;
//   • FileSizeBytes always == the input length;
//   • a cancellation propagates as OperationCanceledException (not the fail-soft path).
[Xunit.Collection("QuestPdfRendering")]
public class PdfPigCvLayoutAnalyzerTests
{
    private readonly PdfPigCvLayoutAnalyzer _sut = new();

    // The 1 cm cramped-margin floor E2 keys off (rubric v2.1 minMarginPointsFloor).
    private const double OneCentimetreInPoints = 28.35;

    // QuestPDF requires the licence declared once before any document is generated. Set here
    // (idempotent; CvRenderer also sets it) so the test-owned generation path is covered too.
    static PdfPigCvLayoutAnalyzerTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    // A paragraph long enough to wrap into full-width lines (so the horizontal margins reflect
    // the page margin, not stray whitespace) yet short enough to stay on ONE A4 page.
    private const string BodyParagraph =
        "Anna Andersson är en erfaren backend-utvecklare med djup kunskap om betalsystem, " +
        "distribuerade arkitekturer och driftsäkerhet i molnet. Hon har lett flera team och " +
        "levererat plattformsmigrationer med mätbara resultat under de senaste åtta åren.";

    // Builds a REAL single-page A4 PDF with the given uniform margin (PDF points) and a
    // full-width body paragraph — so the tightest page-edge margin ≈ the set margin.
    private static byte[] BuildSinglePagePdf(float marginPoints) =>
        QuestDocument.Create(container =>
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(marginPoints); // no Unit ⇒ points (QuestPDF default length unit)
                page.Content().Text(BodyParagraph);
            }))
            .GeneratePdf();

    // Builds a REAL PDF with exactly `pages` physical pages via explicit page breaks.
    private static byte[] BuildMultiPagePdf(int pages) =>
        QuestDocument.Create(container =>
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.Content().Column(col =>
                {
                    for (var i = 1; i <= pages; i++)
                    {
                        col.Item().Text($"Sida {i} — {BodyParagraph}");
                        if (i < pages)
                        {
                            col.Item().PageBreak();
                        }
                    }
                });
            }))
            .GeneratePdf();

    private static byte[] BuildDocx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
            stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in paragraphs)
                body.AppendChild(new Paragraph(new Run(new Text(text))));
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    // ===============================================================
    // (a) A 1-page PDF → PageCount 1, Analyzed, a sensible margin
    // ===============================================================

    [Fact]
    public void Analyze_SinglePagePdf_ReturnsAnalyzedWithOnePageAndAReadableMargin()
    {
        var pdf = BuildSinglePagePdf(marginPoints: 56.7f); // ~2 cm

        var metrics = _sut.Analyze(pdf, CvFileKind.Pdf, CancellationToken.None);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Analyzed);
        metrics.PageCount.ShouldBe(1);
        metrics.FileSizeBytes.ShouldBe(pdf.Length);
        metrics.MinMarginPoints.ShouldNotBeNull();
        // A ~2 cm margin sits comfortably above the 1 cm floor and is not an absurd value — the
        // analyzer read a real geometric margin, not 0 and not the whole page.
        metrics.MinMarginPoints!.Value.ShouldBeGreaterThan(OneCentimetreInPoints);
        metrics.MinMarginPoints!.Value.ShouldBeLessThan(120);
    }

    // ===============================================================
    // (b) A multi-page PDF (≥3) → PageCount reflects the true page tree
    // ===============================================================

    [Fact]
    public void Analyze_MultiPagePdf_ReturnsTheTruePageCount()
    {
        var pdf = BuildMultiPagePdf(pages: 3);

        var metrics = _sut.Analyze(pdf, CvFileKind.Pdf, CancellationToken.None);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Analyzed);
        metrics.PageCount.ShouldBe(3);
    }

    // ===============================================================
    // (c) LOAD-BEARING margin math — narrow (<1 cm) reads below the floor,
    //     wide reads above it. The E2 whitespace verdict keys off exactly this.
    // ===============================================================

    [Fact]
    public void Analyze_NarrowMarginPdf_ReadsBelowTheFloor_WideMarginPdf_ReadsAbove()
    {
        var narrow = _sut.Analyze(
            BuildSinglePagePdf(marginPoints: 8.5f), CvFileKind.Pdf, CancellationToken.None);  // ~0.3 cm
        var wide = _sut.Analyze(
            BuildSinglePagePdf(marginPoints: 85f), CvFileKind.Pdf, CancellationToken.None);   // ~3 cm

        narrow.MinMarginPoints.ShouldNotBeNull();
        wide.MinMarginPoints.ShouldNotBeNull();

        // The whole point of the geometry pass: a cramped layout is measurably below the floor,
        // a roomy one measurably above it — so E2 can Warn on the first and not the second.
        narrow.MinMarginPoints!.Value.ShouldBeLessThan(OneCentimetreInPoints,
            "en marginal på ~0,3 cm ska läsas som knapp (< 1 cm-golvet).");
        wide.MinMarginPoints!.Value.ShouldBeGreaterThan(OneCentimetreInPoints,
            "en marginal på ~3 cm ska läsas som rymlig (> 1 cm-golvet).");
        narrow.MinMarginPoints!.Value.ShouldBeLessThan(wide.MinMarginPoints!.Value,
            "en smalare sidmarginal måste ge ett mindre uppmätt marginalvärde (monotont).");
    }

    // ===============================================================
    // (d) A DOCX → NotApplicable (no conversion, D10); size known, geometry null
    // ===============================================================

    [Fact]
    public void Analyze_Docx_ReturnsNotApplicableWithKnownSizeAndNullGeometry()
    {
        var docx = BuildDocx("Anna Andersson", "Backend-utvecklare på Acme AB");

        var metrics = _sut.Analyze(docx, CvFileKind.Docx, CancellationToken.None);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.NotApplicable);
        metrics.FileSizeBytes.ShouldBe(docx.Length);
        metrics.PageCount.ShouldBeNull();
        metrics.MinMarginPoints.ShouldBeNull();
    }

    // ===============================================================
    // (e) Empty bytes as Pdf → Failed, size 0
    // ===============================================================

    [Fact]
    public void Analyze_EmptyBytesAsPdf_ReturnsFailed()
    {
        var metrics = _sut.Analyze(ReadOnlyMemory<byte>.Empty, CvFileKind.Pdf, CancellationToken.None);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Failed);
        metrics.FileSizeBytes.ShouldBe(0);
        metrics.PageCount.ShouldBeNull();
        metrics.MinMarginPoints.ShouldBeNull();
    }

    // ===============================================================
    // (f) Garbage/non-PDF bytes as Pdf → Failed, NEVER throws, size known
    // ===============================================================

    [Fact]
    public void Analyze_GarbageBytesAsPdf_FailsSoftToFailed_NeverThrows_SizeKnown()
    {
        // A "%PDF" prefix followed by junk — a malformed PDF PdfPig cannot open.
        byte[] garbage = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x00, 0xDE, 0xAD, 0xBE, 0xEF];

        CvLayoutMetrics metrics = null!;
        Should.NotThrow(() => metrics = _sut.Analyze(garbage, CvFileKind.Pdf, CancellationToken.None));

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Failed);
        metrics.FileSizeBytes.ShouldBe(garbage.Length);
    }

    [Fact]
    public void Analyze_TextFileMislabelledAsPdf_FailsSoftToFailed_NeverThrows()
    {
        byte[] notAPdf = System.Text.Encoding.UTF8.GetBytes("det här är ingen PDF, bara text");

        CvLayoutMetrics metrics = null!;
        Should.NotThrow(() => metrics = _sut.Analyze(notAPdf, CvFileKind.Pdf, CancellationToken.None));

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Failed);
        metrics.FileSizeBytes.ShouldBe(notAPdf.Length);
    }

    // ===============================================================
    // (g) FileSizeBytes always == input length, across every arm
    // ===============================================================

    [Fact]
    public void Analyze_FileSizeBytes_AlwaysEqualsTheInputLength_AcrossEveryArm()
    {
        var pdf = BuildSinglePagePdf(56.7f);
        var docx = BuildDocx("Anna Andersson");
        byte[] garbage = [0x25, 0x50, 0x44, 0x46, 0xFF, 0xFF];

        _sut.Analyze(pdf, CvFileKind.Pdf, CancellationToken.None).FileSizeBytes.ShouldBe(pdf.Length);
        _sut.Analyze(docx, CvFileKind.Docx, CancellationToken.None).FileSizeBytes.ShouldBe(docx.Length);
        _sut.Analyze(garbage, CvFileKind.Pdf, CancellationToken.None).FileSizeBytes.ShouldBe(garbage.Length);
    }

    // ===============================================================
    // Cancellation propagates (never swallowed into Failed) — #272 SEC-2 parity
    // ===============================================================

    [Fact]
    public void Analyze_WithPreCancelledToken_ThrowsOperationCanceled_NotSwallowedToFailed()
    {
        var pdf = BuildSinglePagePdf(56.7f);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Should.Throw<OperationCanceledException>(
            () => _sut.Analyze(pdf, CvFileKind.Pdf, cts.Token));
    }

    // ===============================================================
    // (h) FontRuns (#891, ADR 0108) — the reader actually tallies (name, pt) runs
    // ===============================================================

    // A full-width body paragraph at a KNOWN font size. QuestPDF's bundled default family is Lato,
    // embedded in the PDF regardless of OS (deterministic) — and Lato is NOT in the D3 allowlist.
    private static byte[] BuildBodyPdf(int fontSize) =>
        QuestDocument.Create(container =>
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.Content().Text(BodyParagraph).FontSize(fontSize);
            }))
            .GeneratePdf();

    [Fact]
    public void Analyze_TextPdf_CollectsFontRuns_WithTheBodySizeAsTheModalRun()
    {
        var pdf = BuildBodyPdf(fontSize: 11);

        var metrics = _sut.Analyze(pdf, CvFileKind.Pdf, CancellationToken.None);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Analyzed);
        metrics.FontRuns.ShouldNotBeNull();
        metrics.FontRuns.ShouldNotBeEmpty();

        // The modal run (most letters) is the body text rendered at the set size — the reader is
        // not a no-op; it read the point size back from the real glyph geometry.
        var modal = metrics.FontRuns!.MaxBy(run => run.LetterCount)!;
        modal.PointSize.ShouldBe(11);

        // A REAL family name was read: it normalises to a non-empty token, and that token is NOT
        // one of the D3 allowlist entries (Lato is a real, readable, non-standard font). The raw
        // name may carry an embedding subset tag, so assert on the NORMALISED family, never the raw.
        var normalized = FontNameNormalizer.Normalize(modal.FontName);
        normalized.ShouldNotBeNullOrEmpty("analysatorn ska ha läst ett riktigt typsnittsnamn, inte tomt.");
        var allowlistTokens = RealCvConventionsProvider().GetConventions().FontAllowlist
            .Select(FontNameNormalizer.Normalize).ToHashSet();
        allowlistTokens.ShouldNotContain(normalized, "QuestPDF:s standardtypsnitt (Lato) står inte på allowlistan.");

        // The letters actually tally: the summed run counts track the body paragraph's letters.
        metrics.FontRuns!.Sum(run => run.LetterCount).ShouldBeGreaterThan(100);
    }

    [Fact]
    public void Analyze_Docx_HasNullFontRuns()
    {
        // Parity the margin: a DOCX is not geometry-analysed (D10) → no font runs.
        var docx = BuildDocx("Anna Andersson", "Backend-utvecklare på Acme AB");

        var metrics = _sut.Analyze(docx, CvFileKind.Docx, CancellationToken.None);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.NotApplicable);
        metrics.FontRuns.ShouldBeNull();
    }

    [Fact]
    public void Analyze_EmptyBytesAsPdf_HasNullFontRuns()
    {
        var metrics = _sut.Analyze(ReadOnlyMemory<byte>.Empty, CvFileKind.Pdf, CancellationToken.None);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Failed);
        metrics.FontRuns.ShouldBeNull();
    }

    [Fact]
    public void Analyze_GarbageBytesAsPdf_HasNullFontRuns()
    {
        byte[] garbage = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x00, 0xDE, 0xAD, 0xBE, 0xEF];

        var metrics = _sut.Analyze(garbage, CvFileKind.Pdf, CancellationToken.None);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Failed);
        metrics.FontRuns.ShouldBeNull();
    }

    // ===============================================================
    // (i) END-TO-END — analyzer → real engine → D3 (the FORM PdfPig emits)
    // ===============================================================

    [Fact]
    public async Task Analyze_TextPdf_ThenRealEngine_D3Warns_BecauseLatoIsNotAllowlisted()
    {
        // "The oracle runs the FORM production emits (PdfPig Letters)": build a REAL PDF
        // (QuestPDF/Lato), run the REAL PdfPigCvLayoutAnalyzer, feed its CvLayoutMetrics into the
        // REAL CvReviewEngine and assert D3. Lato is a real, readable font that is NOT on the
        // exemplar allowlist → Warn (never Pass; never NotAssessed — font runs WERE collected).
        var engine = new CvReviewEngine(
            RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

        var pdf = BuildBodyPdf(fontSize: 11);
        var metrics = _sut.Analyze(pdf, CvFileKind.Pdf, CancellationToken.None);

        var result = await engine.ReviewAsync(
            CvReviewContext.FromParsed(Resume(layoutMetrics: metrics)),
            RenderProfile.Ats, TestContext.Current.CancellationToken);
        var d3 = result.Verdicts.Single(v => v.CriterionId == "D3");

        d3.Verdict.ShouldNotBe(CriterionVerdict.NotAssessed, "font runs samlades in ur PDF:en, D3 kan bedömas.");
        d3.Verdict.ShouldNotBe(CriterionVerdict.Pass, "Lato är läsbart men står inte på allowlistan.");
        d3.Verdict.ShouldBe(CriterionVerdict.Warn);
    }
}
