using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Parsing.DocxRevisionMarkup;
// `Document` is ambiguous between QuestPDF and OpenXml (both are used here — QuestPDF builds
// the PDF fixtures, OpenXml the DOCX ones). Alias the QuestPDF one; OpenXml's `Document` stays
// bare in BuildDocx. Same convention as PdfPigCvLayoutAnalyzerTests.
using QuestDocument = QuestPDF.Fluent.Document;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

// Fas 4 STEG 8 (F4-8, NO AI/LLM) — PdfPigOpenXmlCvTextExtractor is the format→text port
// impl (PdfPig/OpenXml confined behind it; internal, visible via InternalsVisibleTo).
// Contract: fail-SOFT — a corrupt/empty file NEVER throws, it returns
// Empty/NoTextLayer so the handler routes to manual fallback (OQ5). The DOCX path is
// exercised with a synthesized in-memory WordprocessingDocument (incl. åäö).
//
// #1060 PR E — the PDF path now has a HAPPY PATH here, and the comment that used to stand in
// its place ("synthesizing a VALID PDF in-memory is impractical … a faked valid PDF would not
// be a real test") was false when written and is deleted. This project has referenced QuestPDF
// since Fas 4b PR-6b precisely so PDFs can be synthesized for real, and the .csproj says so:
// "the margin-math is only honestly testable against genuine PDF bytes, never a faked buffer".
// PdfPigCvLayoutAnalyzerTests has been doing it in this same assembly ever since. The cost of
// the false comment was measured: until #1060 the extractor's ONLY PDF coverage was
// Extract_MalformedBytesAsPdf_StatusEmpty_NeverThrows, so every behavioural change to the PDF
// path shipped unmeasured (CTO-bind 2026-07-27 §C.1).
[Xunit.Collection("QuestPdfRendering")]
public class PdfPigOpenXmlCvTextExtractorTests
{
    private readonly PdfPigOpenXmlCvTextExtractor _sut = new();

    // QuestPDF requires the licence declared once before any document is generated. Idempotent;
    // PdfPigCvLayoutAnalyzerTests and CvRenderer set it too.
    static PdfPigOpenXmlCvTextExtractorTests() =>
        QuestPDF.Settings.License = LicenseType.Community;

    private static byte[] BuildDocx(params string[] paragraphs) =>
        BuildDocxFromParagraphs([.. paragraphs.Select(text => new Paragraph(new Run(new Text(text))))]);

    // ---- PDF fixtures (#1060 PR E) ------------------------------------------------------
    // A REAL single-column Swedish CV, rendered with QuestPDF. The employers are the markers
    // the assertions trace; åäö is load-bearing (§10 — UTF-8 must survive the whole chain).

    /// <summary>One rendered line, and the extra vertical space authored ABOVE it (points).
    /// <c>GapAbove: 0</c> means "the next line of the same block" — ordinary leading.</summary>
    private readonly record struct CvLine(string Text, int GapAbove);

    // Section gaps 18 pt, in-block gaps 6 pt, employment gaps 14 pt, everything else ordinary
    // leading. This is what a word processor emits for a CV with paragraph spacing; the
    // *absence* of that spacing is a different document and has its own fixture below.
    private static readonly CvLine[] SpacedCv =
    [
        new("Anna Andersson", 0),
        new("anna.andersson@example.com | 070-123 45 67", 0),
        new("Göteborg", 0),
        new("PROFIL", 18),
        new("Erfaren backend-utvecklare med djup kunskap om betalsystem.", 6),
        new("ARBETSLIVSERFARENHET", 18),
        new("Senior backend-utvecklare — Klarna AB", 6),
        new("2021 - 2026", 0),
        new("Ledde teamet för betalflöden.", 0),
        new("Backend-utvecklare — Volvo Cars", 14),
        new("2018 - 2021", 0),
        new("Byggde tjänster för uppkopplade fordon.", 0),
        new("Systemutvecklare — Västra Götalandsregionen", 14),
        new("2015 - 2018", 0),
        new("Journalsystem i .NET.", 0),
        new("UTBILDNING", 18),
        new("Civilingenjör Datateknik — Chalmers", 6),
        new("2010 - 2015", 0),
    ];

    private static readonly string[] SpacedCvEmployers =
        ["Klarna AB", "Volvo Cars", "Västra Götalandsregionen"];

    /// <summary>The same CV with paragraph spacing ALSO inside each employment — above the period
    /// line and above the description line. A word processor produces this whenever those lines
    /// are ordinary paragraphs rather than line breaks, which is the common case.
    ///
    /// <para>It exists because a geometry-derived boundary rule was built for #1060 PR E, passed
    /// every fixture then available, and was measured on this shape to split entries apart —
    /// yielding fragments with no organization, which <c>Resume.ValidateContent</c> rejects,
    /// turning a CV that promoted into a hard block. Nothing in the suite could exhibit that,
    /// because every fixture put spacing only BETWEEN employments. The rule was withdrawn; this
    /// fixture is what makes the next attempt measurable.</para></summary>
    private static readonly CvLine[] IntraBlockSpacedCv = BuildIntraBlockSpacedCv();

    /// <summary>Adds Word's 8 pt space-after above every line that CONTINUES an entry — i.e. every
    /// line the fixture authored at ordinary leading (<c>GapAbove == 0</c>) other than the identity
    /// block at the top.
    ///
    /// <para>Derived from <see cref="SpacedCv"/>'s own structure rather than by matching literal
    /// strings. A string-matching version was written first and is the wrong shape: correcting a
    /// typo in one of those literals would silently stop selecting it, degrading this fixture
    /// toward a copy of <see cref="SpacedCv"/> with nothing turning red. The count assertion below
    /// makes the degradation impossible rather than unlikely.</para></summary>
    private static CvLine[] BuildIntraBlockSpacedCv()
    {
        // Everything from the first section heading onward; the identity lines above it are one
        // block by construction and must stay at ordinary leading.
        var firstHeading = Array.FindIndex(SpacedCv, l => l.GapAbove > 0);

        var lines = SpacedCv
            .Select((l, i) => i > firstHeading && l.GapAbove == 0 ? l with { GapAbove = 8 } : l)
            .ToArray();

        var changed = lines.Count(l => l.GapAbove > 0) - SpacedCv.Count(l => l.GapAbove > 0);
        if (changed < 5)
        {
            throw new InvalidOperationException(
                $"IntraBlockSpacedCv must add spacing to at least 5 continuation lines; it added {changed}. "
                + "SpacedCv's shape changed and this fixture has silently degraded toward a copy of it.");
        }

        return lines;
    }

    private static byte[] BuildCvPdf(IReadOnlyList<CvLine> lines) =>
        QuestDocument.Create(container =>
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(11));
                page.Content().Column(col =>
                {
                    foreach (var line in lines)
                        col.Item().PaddingTop(line.GapAbove).Text(line.Text);
                });
            })).GeneratePdf();

    /// <summary>The same content with EVERY authored gap removed — uniform leading end to end.
    /// The document carries no paragraph signal, so nothing may be inferred from it.</summary>
    private static byte[] BuildUniformLeadingCvPdf() =>
        BuildCvPdf([.. SpacedCv.Select(l => l with { GapAbove = 0 })]);

    [Fact]
    public void Extract_SynthesizedDocx_StatusExtracted_RawTextContainsParagraphs()
    {
        var bytes = BuildDocx("Anna Andersson", "Backend-utvecklare på Acme AB");

        var result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        result.RawText.ShouldContain("Anna Andersson");
        result.RawText.ShouldContain("Backend-utvecklare på Acme AB");
    }

    [Fact]
    public void Extract_DocxWithSwedishCharacters_PreservesAaO()
    {
        // åäö must survive extraction/serialization (CLAUDE.md §10 — UTF-8 everywhere).
        var bytes = BuildDocx("Förskollärare", "Erfarenhet av kärnverksamhet på äldreboende");

        var result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        result.RawText.ShouldContain("Förskollärare");
        result.RawText.ShouldContain("äldreboende");
    }

    [Fact]
    public void Extract_EmptyBytesAsDocx_StatusEmpty_NoThrow()
    {
        var result = _sut.Extract(ReadOnlyMemory<byte>.Empty, CvFileKind.Docx, CancellationToken.None);

        result.Status.ShouldBe(CvExtractionStatus.Empty);
        result.RawText.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_GarbageBytesAsDocx_StatusEmpty_NeverThrows()
    {
        byte[] garbage = [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x01];

        CvExtractionResult result = default!;
        Should.NotThrow(() => result = _sut.Extract(garbage, CvFileKind.Docx, CancellationToken.None));

        result.Status.ShouldBe(CvExtractionStatus.Empty);
    }

    // #268 SEC-1 — a valid ZIP/OPC package with one entry that DECLARES a huge uncompressed
    // size but compresses to ~KB (a zip bomb). The pre-flight package-size guard must reject it
    // (Empty) without inflating it (no OOM). Written by streaming a repeated byte so the test
    // itself stays low-memory.
    private static byte[] BuildZipWithOversizedEntry(string entryName, long uncompressedBytes)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var es = entry.Open();
            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)' ');
            long written = 0;
            while (written < uncompressedBytes)
            {
                var n = (int)Math.Min(chunk.Length, uncompressedBytes - written);
                es.Write(chunk, 0, n);
                written += n;
            }
        }

        return ms.ToArray();
    }

    [Fact]
    public void Extract_DocxDeclaringOversizedUncompressedSize_FailsSoftEmpty_NoOom()
    {
        // 70 MiB declared uncompressed (> the 64 MiB ceiling), a few hundred KB compressed.
        var bomb = BuildZipWithOversizedEntry("word/document.xml", 70L * 1024 * 1024);
        bomb.Length.ShouldBeLessThan(2 * 1024 * 1024,
            "Förutsättning: bomben ska vara liten komprimerad (annars testar vi fel sak).");

        CvExtractionResult result = default!;
        Should.NotThrow(() => result = _sut.Extract(bomb, CvFileKind.Docx, CancellationToken.None));

        result.Status.ShouldBe(CvExtractionStatus.Empty,
            "En DOCX vars deklarerade okomprimerade storlek överstiger taket ska avvisas " +
            "fail-soft FÖRE dekomprimering (#268 SEC-1 zip-bomb-guard).");
        result.RawText.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_DocxWithSingleOversizedTextNode_TruncatedToCap_NoOom()
    {
        // One <w:t> node larger than the 1M output-char cap. Under the package-size ceiling, so
        // the pre-flight guard passes; the per-node truncation must cap the output rather than
        // append the whole node, and never throw.
        var huge = new string('a', 1_500_000);
        var bytes = BuildDocx(huge);

        CvExtractionResult result = default!;
        Should.NotThrow(() => result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None));

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        result.RawText.Length.ShouldBeLessThanOrEqualTo(1_000_000,
            "Output ska kapas vid MaxOutputChars även för en enda enorm textnod (#268 SEC-1).");
        result.RawText.Length.ShouldBeGreaterThan(900_000,
            "Och den ska faktiskt extrahera (inte fail-soft till tomt) under taket.");
    }

    [Fact]
    public void Extract_ValidZipButNotADocx_FailsSoftEmpty_NeverThrows()
    {
        // A valid ZIP with no word/document.xml (a renamed archive / degenerate OPC). The
        // pre-flight guard must iterate the central directory without crashing (total under the
        // ceiling → no rejection), then WordprocessingDocument.Open / the null-main-part branch
        // fail soft → Empty. Proves the guard is safe on a degenerate-but-valid zip.
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var es = entry.Open();
            es.Write("not a docx"u8);
        }

        var notADocx = ms.ToArray();

        CvExtractionResult result = default!;
        Should.NotThrow(() => result = _sut.Extract(notADocx, CvFileKind.Docx, CancellationToken.None));

        result.Status.ShouldBe(CvExtractionStatus.Empty);
        result.RawText.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_NormalSizedDocx_NotRejectedByZipBombGuard()
    {
        // A real CV is well under the ceiling — the guard must not produce false rejections.
        var bytes = BuildDocx("Anna Andersson", "Systemutvecklare", "C#, PostgreSQL, Docker");

        var result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        result.RawText.ShouldContain("Systemutvecklare");
    }

    // #1060 PR E — THE PDF HAPPY PATH. Until this test the PDF branch had no happy-path
    // coverage at all: the only PDF case in the suite was the malformed-bytes one below, so
    // "a real CV comes out of a real PDF" was an unmeasured claim through two phases. Asserted
    // against GENUINE PDF bytes (QuestPDF), not a faked buffer.
    [Fact]
    public void Extract_SynthesizedSingleColumnCvPdf_StatusExtracted_CarriesEveryEmployerAndAaO()
    {
        var bytes = BuildCvPdf(SpacedCv);

        var result = _sut.Extract(bytes, CvFileKind.Pdf, CancellationToken.None);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);

        // Every authored employer survives extraction verbatim. This is the marker trace the
        // corpus runs end-to-end, asserted here at the one layer that owns bytes→text.
        foreach (var employer in SpacedCvEmployers)
            result.RawText.ShouldContain(employer);

        // åäö must survive the whole chain (§10 — UTF-8 everywhere). "Västra Götalandsregionen"
        // above already carries them; these pin the lower-case forms too.
        result.RawText.ShouldContain("Göteborg");
        result.RawText.ShouldContain("betalflöden");
        result.RawText.ShouldContain("tjänster");
    }

    /// <summary>The three authored spacings this base pin covers. Dispatch is on the ENUM, never
    /// on a display string: an earlier revision switched on the label with a catch-all arm, so
    /// editing one label would have silently routed two cases to the same document and left the
    /// suite green with a control it was no longer running.</summary>
    public enum CvSpacing
    {
        /// <summary>No authored spacing anywhere — the document states no boundary.</summary>
        UniformLeading,

        /// <summary>Paragraph spacing between employments only.</summary>
        BetweenEntries,

        /// <summary>Paragraph spacing between AND inside employments.</summary>
        BetweenAndInsideEntries,
    }

    // #1060 PR E — THE BASE, pinned. Three documents with the same words and three different
    // authored spacings. `ContentOrderTextExtractor.GetText(page)` emits one newline per visual
    // line regardless, so all three produce the same shape — zero blank lines — and the segmenter,
    // which splits entries on blank lines and nothing else, cannot tell them apart. That is #1060
    // stated at the layer that owns it.
    //
    // WHAT A FUTURE BOUNDARY FIX MUST DO TO THESE ROWS, stated precisely because getting it wrong
    // is how the next attempt repeats this one:
    //
    //   BetweenEntries          MUST go red. The document states a boundary; a fix must find it.
    //   BetweenAndInsideEntries MUST go red — and see below for the part that is NOT about colour.
    //   UniformLeading          MUST STAY GREEN, PERMANENTLY. This document authors no boundary at
    //                           all, so a rule that emits a blank line here has INVENTED one. The
    //                           withdrawn rule preserved this property and it is the one property
    //                           that must never be traded for recall.
    //
    // The third row needs more than a colour: a rule may legitimately place a boundary between its
    // employments while placing NONE between a period line and its own description line. Turning it
    // red is necessary and not sufficient — the withdrawn rule turned it red by splitting entries
    // apart, which is precisely the regression. Judge that row by the corpus arms
    // `pdf-single-column-intra-block-spaced[-tight-list]`, whose parsed-entry counts say whether
    // the boundaries landed between entries or inside them.
    [Theory]
    [InlineData(CvSpacing.UniformLeading)]
    [InlineData(CvSpacing.BetweenEntries)]
    [InlineData(CvSpacing.BetweenAndInsideEntries)]
    public void Extract_PdfSpacingIsInvisibleInTheExtractedText_TodaysBase(CvSpacing shape)
    {
        var bytes = shape switch
        {
            CvSpacing.UniformLeading => BuildUniformLeadingCvPdf(),
            CvSpacing.BetweenEntries => BuildCvPdf(SpacedCv),
            CvSpacing.BetweenAndInsideEntries => BuildCvPdf(IntraBlockSpacedCv),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var result = _sut.Extract(bytes, CvFileKind.Pdf, CancellationToken.None);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        result.RawText.Split('\n').ShouldAllBe(l => l.Trim().Length > 0,
            $"the extractor emits no blank line for '{shape}' — authored spacing is discarded");
    }

    [Fact]
    public void Extract_MalformedBytesAsPdf_StatusEmpty_NeverThrows()
    {
        // The fail-soft contract for a malformed PDF: never throws, reports Empty. (The happy
        // PDF path is the test above — it is no longer stood in for by the DOCX path.)
        byte[] malformed = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x00, 0xDE, 0xAD];

        CvExtractionResult result = default!;
        Should.NotThrow(() => result = _sut.Extract(malformed, CvFileKind.Pdf, CancellationToken.None));

        result.Status.ShouldBe(CvExtractionStatus.Empty);
        result.RawText.ShouldBeEmpty();
    }

    // #272 SEC-1 — a DOCX whose main part decompresses to far more than the hard byte/char
    // bound (the "lying" bomb: declared size under the 64 MiB pre-flight ceiling, but the
    // actual inflated content is large). The byte-cap / MaxCharactersInDocument bound it
    // DURING inflation → fail-soft Empty, never a full materialization, never throws out.
    [Fact]
    public void Extract_DocxWithLargeInflatedMainPart_FailsSoftEmpty_BoundedDuringInflation()
    {
        // 20M-char single text node: word/document.xml inflates to ~20 MB (declared < 64 MiB
        // pre-flight, but past the 16 MiB byte-cap / 4M-char document ceiling). Compresses
        // tiny (repeated char) so the test stays low-memory on the COMPRESSED side.
        var bytes = BuildDocx(new string('a', 20_000_000));
        bytes.Length.ShouldBeLessThan(2 * 1024 * 1024,
            "Förutsättning: liten komprimerad input (annars testar vi fel sak).");

        CvExtractionResult result = default!;
        Should.NotThrow(() => result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None));

        result.Status.ShouldBe(CvExtractionStatus.Empty,
            "En DOCX vars huvuddel inflateras förbi byte-/char-taket ska avvisas fail-soft " +
            "UNDER inflatering (#272 SEC-1 ljugande zip-bomb).");
        result.RawText.ShouldBeEmpty();
    }

    // #272 SEC-1 — XXE / billion-laughs / external-entity hardening. A crafted DOCX whose
    // document.xml declares a DTD + entity. The hardened XmlReader (DtdProcessing.Prohibit,
    // XmlResolver=null) must reject it → fail-soft Empty, never expand the entity, never
    // fetch anything, never throw out to the caller.
    [Fact]
    public void Extract_DocxWithDtdDoctype_FailsSoftEmpty_NeverExpandsEntities()
    {
        const string maliciousDocument =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<!DOCTYPE x [ <!ENTITY lol \"ENTITY-EXPANDED-PAYLOAD\"> ]>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:body><w:p><w:r><w:t>&lol;</w:t></w:r></w:p></w:body></w:document>";
        var bytes = BuildMinimalOpcWithDocumentXml(maliciousDocument);

        CvExtractionResult result = default!;
        Should.NotThrow(() => result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None));

        result.Status.ShouldBe(CvExtractionStatus.Empty,
            "En DOCX vars document.xml bär en DTD/entitet ska avvisas fail-soft (XXE-härdning #272 SEC-1).");
        result.RawText.ShouldBeEmpty();
        // Explicit anti-leak oracle: the entity is NEVER expanded into output (billion-laughs/XXE).
        result.RawText.ShouldNotContain("ENTITY-EXPANDED-PAYLOAD");
    }

    // #272 SEC-1 — the hand-rolled XmlReader walk must reconstruct text across MULTIPLE runs
    // in a paragraph (Word splits a sentence into several <w:r><w:t>) and preserve a
    // significant (xml:space="preserve") whitespace-only run between them (the
    // SignificantWhitespace branch). Built with a raw document.xml so the structure is exact.
    [Fact]
    public void Extract_DocxWithMultipleRunsAndPreservedSpace_ConcatenatesWithWhitespace()
    {
        const string doc =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:body><w:p>" +
            "<w:r><w:t>C#</w:t></w:r>" +
            "<w:r><w:t xml:space=\"preserve\"> </w:t></w:r>" +
            "<w:r><w:t>PostgreSQL</w:t></w:r>" +
            "</w:p></w:body></w:document>";
        var bytes = BuildMinimalOpcWithDocumentXml(doc);

        var result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        // Multiple runs concatenated + a significant (preserved) whitespace run kept (#272 SEC-1).
        result.RawText.ShouldContain("C# PostgreSQL");
    }

    // #272 SEC-1 — a self-closing empty <w:t/> (IsEmptyElement branch) must not flip the
    // insideText flag in a way that swallows a following paragraph's text.
    [Fact]
    public void Extract_DocxWithEmptySelfClosingTextNode_DoesNotSwallowFollowingText()
    {
        const string doc =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:body>" +
            "<w:p><w:r><w:t/></w:r></w:p>" +
            "<w:p><w:r><w:t>Andra stycket</w:t></w:r></w:p>" +
            "</w:body></w:document>";
        var bytes = BuildMinimalOpcWithDocumentXml(doc);

        var result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        // An empty <w:t/> (IsEmptyElement) must not swallow a following paragraph's text (#272).
        result.RawText.ShouldContain("Andra stycket");
    }

    // #272 SEC-2 — a cancellation must propagate as OperationCanceledException, NOT be
    // swallowed into the fail-soft Empty path (a timeout must not masquerade as a bad file).
    [Fact]
    public void Extract_DocxWithPreCancelledToken_ThrowsOperationCanceled_NotSwallowedToEmpty()
    {
        var bytes = BuildDocx("Anna Andersson", "Utvecklare");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Should.Throw<OperationCanceledException>(
            () => _sut.Extract(bytes, CvFileKind.Docx, cts.Token));
    }

    // #1741 — Word writes Shift+Enter as <w:br/> and a tab as <w:tab/> inside the run (ECMA-376
    // §17.3.3.1, §17.3.3.32). A line break and a carriage return end a line as a paragraph does; a tab,
    // a positioned tab and a symbol separate their neighbours.
    public static TheoryData<string> LineBreaks() => ["br", "br page", "br column", "cr"];

    [Theory]
    [MemberData(nameof(LineBreaks))]
    public void Extract_DocxContactBlockWrittenWithLineBreaks_IsTheSameTextAsThreeParagraphs(string lineBreak)
    {
        var bytes = BuildDocxFromParagraphs(new Paragraph(new Run(
            new Text("Anna Andersson"), RunElement(lineBreak),
            new Text("811218-9876"), RunElement(lineBreak),
            new Text("070-123 45 67"))));
        var threeParagraphs = BuildDocx("Anna Andersson", "811218-9876", "070-123 45 67");

        var result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe(
            _sut.Extract(threeParagraphs, CvFileKind.Docx, CancellationToken.None).RawText);
        FlagCount(result.RawText).ShouldBe(1);
        PersonnummerRedactor.Redact(result.RawText).ShouldNotContain("811218-9876");
    }

    public static TheoryData<string> Separators() => ["tab", "ptab", "sym"];

    [Theory]
    [MemberData(nameof(Separators))]
    public void Extract_DocxFieldsSeparatedInTheRun_AreSeparatedBySpaces(string separator)
    {
        var bytes = BuildDocxFromParagraphs(new Paragraph(new Run(
            new Text("070-123 45 67"), RunElement(separator),
            new Text("811218-9876"), RunElement(separator),
            new Text("070-765 43 21"))));

        var result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("070-123 45 67 811218-9876 070-765 43 21");
        FlagCount(result.RawText).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxTabsInsideAPersonnummer_AreOneSpaceTheBridgeSpans()
    {
        var bytes = BuildDocxFromParagraphs(new Paragraph(new Run(
            new Text("811218"), new TabChar(), new TabChar(), new TabChar(), new Text("9876"))));

        var result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("811218 9876");
        FlagCount(result.RawText).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxLineBreakInsideAPersonnummer_SplitsIt()
    {
        // The residual ADR 0134 D1 accepts for every line break in a CV, as at </w:p>.
        var bytes = BuildDocxFromParagraphs(new Paragraph(new Run(
            new Text("811218-"), new Break(), new Text("9876"))));

        var result = _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("811218-\n9876");
        FlagCount(result.RawText).ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Separators))]
    public void Extract_DocxSeparatorAtTheStartOfALine_YieldsNothing(string separator)
    {
        var bytes = BuildDocxFromParagraphs(
            new Paragraph(new Run(RunElement(separator), new Text("Rad ett"))),
            new Paragraph(new Run(RunElement(separator), new Text("Rad två"))));

        _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None).RawText.ShouldBe("Rad ett\nRad två");
    }

    [Fact]
    public void Extract_DocxEmptyTabStopList_LeavesTheRunTabAfterItASeparator()
    {
        var bytes = BuildDocxFromParagraphs(new Paragraph(
            new ParagraphProperties(new Tabs()),
            new Run(new Text("070-123 45 67"), new TabChar(), new Text("811218-9876"))));

        // A self-closing element has no end element.
        MainDocumentXml(bytes).ShouldContain("<w:tabs />");
        _sut.Extract(bytes, CvFileKind.Docx, CancellationToken.None).RawText.ShouldBe("070-123 45 67 811218-9876");
    }

    [Fact]
    public void Extract_DocxBreakAndTabWithEndElements_YieldOneCharacterEach()
    {
        // An empty element may be written with an end tag (XML 1.0 §3.1); System.Xml.Linq writes it so.
        var br = new XElement(WordMain + "br", string.Empty).ToString(SaveOptions.DisableFormatting);
        var tab = new XElement(WordMain + "tab", string.Empty).ToString(SaveOptions.DisableFormatting);
        br.ShouldEndWith("></br>");
        tab.ShouldEndWith("></tab>");
        var doc = DocumentXml(
            "<w:p><w:r><w:t>Rad ett</w:t>" + br + "<w:t>070-123 45 67</w:t>" +
            tab + "<w:t>811218-9876</w:t></w:r></w:p>");

        var result = _sut.Extract(BuildMinimalOpcWithDocumentXml(doc), CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("Rad ett\n070-123 45 67 811218-9876");
    }

    [Fact]
    public void Extract_DocxBreakAndTabInAnotherNamespace_YieldNothing()
    {
        // DrawingML, the text language of shapes, has a br and a tab of its own.
        var doc = DocumentXml("<w:p><w:r><w:t>Rad</w:t><a:br/><a:tab/><w:t>ett</w:t></w:r></w:p>");

        var result = _sut.Extract(BuildMinimalOpcWithDocumentXml(doc), CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("Radett");
    }

    [Fact]
    public void Extract_DocxTextBox_RunTabAfterItsTabStopsYieldsASpace()
    {
        // A text box is run content of the paragraph that anchors it, so its own paragraphs, tab-stop
        // definitions included, sit inside an open <w:r>. This is its VML form.
        var doc = DocumentXml(
            "<w:p><w:r><w:t>Rad ett</w:t></w:r></w:p>" +
            "<w:p><w:r><w:pict><v:shape><v:textbox><w:txbxContent>" +
            "<w:p><w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"4536\"/></w:tabs></w:pPr>" +
            "<w:r><w:t>Rad två</w:t><w:tab/><w:t>811218-9876</w:t></w:r></w:p>" +
            "</w:txbxContent></v:textbox></v:shape></w:pict></w:r></w:p>");

        var result = _sut.Extract(BuildMinimalOpcWithDocumentXml(doc), CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("Rad ett\nRad två 811218-9876");
    }

    public static TheoryData<string> BreaksBesideALineEnd() => ["two breaks in a run", "a break closing a paragraph"];

    [Theory]
    [MemberData(nameof(BreaksBesideALineEnd))]
    public void Extract_DocxBreakBesideAnotherLineEnd_YieldsABlankLine(string shape)
    {
        Paragraph[] paragraphs = shape switch
        {
            "two breaks in a run" => [new Paragraph(new Run(new Text("A"), new Break(), new Break(), new Text("B")))],
            "a break closing a paragraph" =>
                [new Paragraph(new Run(new Text("A"), new Break())), new Paragraph(new Run(new Text("B")))],
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        _sut.Extract(BuildDocxFromParagraphs(paragraphs), CvFileKind.Docx, CancellationToken.None)
            .RawText.ShouldBe("A\n\nB");
    }

    // #1801 — Word writes a text box or an image as VML (w:pict) or as DrawingML (w:drawing, inline or
    // anchored), often both at once inside mc:AlternateContent.
    public static TheoryData<string> TextBoxForms() => ["vml floating", "vml inline", "drawing anchor", "drawing inline"];

    [Theory]
    [MemberData(nameof(TextBoxForms))]
    public void Extract_DocxTextBoxAfterText_StartsItsOwnLine(string form)
    {
        var raw = ExtractXml(
            "<w:p><w:r><w:t>811218-9876</w:t></w:r><w:r>" + TextBox(form, "070-123 45 67") + "</w:r></w:p>");

        raw.ShouldBe(IsInline(form) ? "811218-9876 \n070-123 45 67" : "811218-9876\n070-123 45 67");
        FlagCount(raw).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(TextBoxForms))]
    public void Extract_DocxTextBoxAfterAPhoneNumber_StartsItsOwnLine(string form)
    {
        var raw = ExtractXml(
            "<w:p><w:r><w:t>Tel 070-12 34 567</w:t></w:r><w:r>" + TextBox(form, "811218-9876") + "</w:r></w:p>");

        raw.ShouldBe(IsInline(form) ? "Tel 070-12 34 567 \n811218-9876" : "Tel 070-12 34 567\n811218-9876");
        FlagCount(raw).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(TextBoxForms))]
    public void Extract_DocxOpeningWithATextBox_ExtractsIt(string form)
    {
        var raw = ExtractXml("<w:p><w:r>" + TextBox(form, "811218-9876") + "</w:r></w:p>");

        raw.ShouldBe("811218-9876");
        FlagCount(raw).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(TextBoxForms))]
    public void Extract_DocxTextBoxOpeningAParagraph_AddsNoBlankLine(string form)
    {
        var raw = ExtractXml("<w:p><w:r><w:t>Rad ett</w:t></w:r></w:p><w:p><w:r>" + TextBox(form, "Box") + "</w:r></w:p>");

        raw.ShouldBe("Rad ett\nBox");
    }

    [Theory]
    [MemberData(nameof(TextBoxForms))]
    public void Extract_DocxTextBoxAfterATab_StillStartsItsOwnLine(string form)
    {
        var raw = ExtractXml(
            "<w:p><w:r><w:t>Tel</w:t><w:tab/></w:r><w:r>" + TextBox(form, "811218-9876") + "</w:r></w:p>");

        raw.ShouldBe("Tel \n811218-9876");
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public void Extract_DocxTextBoxInAlternateContent_StartsItsOwnLineInEveryBranch(string personnummer)
    {
        // Both branches are read today (#1801, point 2), so the oracle holds whether one or both are.
        var (anchor, inBox) = personnummer switch
        {
            "before" => ("811218-9876", "070-123 45 67"),
            "after" => ("Tel 070-12 34 567", "811218-9876"),
            _ => throw new ArgumentOutOfRangeException(nameof(personnummer), personnummer, null),
        };
        var raw = ExtractXml(
            "<w:p><w:r><w:t>" + anchor + "</w:t></w:r><w:r><mc:AlternateContent>" +
            "<mc:Choice Requires=\"wps\">" + TextBox("drawing anchor", inBox) + "</mc:Choice>" +
            "<mc:Fallback>" + TextBox("vml floating", inBox) + "</mc:Fallback>" +
            "</mc:AlternateContent></w:r></w:p>");

        raw.Split('\n').Where(line => line.Contains("811218")).ShouldAllBe(line => line == "811218-9876");
        var occurrences = raw.Split("811218-9876").Length - 1;
        occurrences.ShouldBeGreaterThanOrEqualTo(1);
        FlagCount(raw).ShouldBe(occurrences);
    }

    public static TheoryData<string> InlineObjectForms() =>
        ["drawing inline", "vml inline", "vml mso-position only", "object"];

    [Theory]
    [MemberData(nameof(InlineObjectForms))]
    public void Extract_DocxInlineObjectBetweenFields_SeparatesThem(string form)
    {
        var raw = ExtractXml(
            "<w:p><w:r><w:t>811218-9876</w:t></w:r><w:r>" + ObjectWithoutText(form) + "</w:r>" +
            "<w:r><w:t>070-123 45 67</w:t></w:r></w:p>");

        raw.ShouldBe("811218-9876 070-123 45 67");
        FlagCount(raw).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(InlineObjectForms))]
    public void Extract_DocxInlineObjectAfterAPhoneNumber_SeparatesThem(string form)
    {
        var raw = ExtractXml(
            "<w:p><w:r><w:t>Tel 070-12 34 567</w:t></w:r><w:r>" + ObjectWithoutText(form) + "</w:r>" +
            "<w:r><w:t>811218-9876</w:t></w:r></w:p>");

        raw.ShouldBe("Tel 070-12 34 567 811218-9876");
        FlagCount(raw).ShouldBe(1);
    }

    public static TheoryData<string> FloatingObjectForms() => ["drawing anchor", "vml floating", "vml shapetype first"];

    [Theory]
    [MemberData(nameof(FloatingObjectForms))]
    public void Extract_DocxFloatingObjectWithoutText_AddsNothing(string form)
    {
        // The next run's properties sit at the depth a VML shape had.
        var withObject = ExtractXml(
            "<w:p><w:r><w:t>Anna</w:t></w:r><w:r>" + ObjectWithoutText(form) + "</w:r>" +
            "<w:r><w:rPr><w:b/></w:rPr><w:t>Andersson</w:t></w:r></w:p>");
        var without = ExtractXml(
            "<w:p><w:r><w:t>Anna</w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>Andersson</w:t></w:r></w:p>");

        withObject.ShouldBe(without);
    }

    // #1810 — Word's other stories reach the personnummer scan through AuxiliaryText, and never RawText.
    private const string AnnaBody = "<w:p><w:r><w:t>Anna Andersson</w:t></w:r></w:p>";

    public static TheoryData<string> OtherStories() => DocxStoryFixture.Stories();

    [Theory]
    [MemberData(nameof(OtherStories))]
    public void Extract_DocxPersonnummerOnlyInAnotherStory_ReachesTheScanButNotRawText(string story)
    {
        var result = ExtractStories(AnnaBody, DocxStoryFixture.Paragraph(story, "811218-9876"));

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        result.RawText.ShouldBe("Anna Andersson");
        result.AuxiliaryText.ShouldBe("811218-9876");
        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> HeaderForms() => ["table cell", "content control", "field result", "text box"];

    [Theory]
    [MemberData(nameof(HeaderForms))]
    public void Extract_DocxPersonnummerInAHeaderForm_ReachesTheScan(string form)
    {
        var content = form switch
        {
            "table cell" => "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>811218-9876</w:t></w:r></w:p></w:tc></w:tr></w:tbl>",
            "content control" => "<w:sdt><w:sdtContent><w:p><w:r><w:t>811218-9876</w:t></w:r></w:p></w:sdtContent></w:sdt>",
            "field result" => "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> PAGE </w:instrText></w:r>" +
                "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>811218-9876</w:t></w:r>" +
                "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>",
            "text box" => "<w:p><w:r>" + TextBox("drawing anchor", "811218-9876") + "</w:r></w:p>",
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };

        var result = ExtractStories(AnnaBody, DocxStoryFixture.Part("header", content));

        result.RawText.ShouldBe("Anna Andersson");
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxThreeHeaderVariants_AreEachRead()
    {
        var result = ExtractStories(AnnaBody,
            DocxStoryFixture.Paragraph("header", "Kontakt"),
            DocxStoryFixture.Paragraph("header", "Utvecklare"),
            DocxStoryFixture.Paragraph("header", "811218-9876"));

        result.AuxiliaryText.Split('\n').ShouldBe(["Kontakt", "Utvecklare", "811218-9876"], ignoreOrder: true);
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxOtherStories_StartOnALineAfterTheMainText()
    {
        // The main text ends in a phone number's digits, and the header starts with the number.
        var result = ExtractStories("<w:p><w:r><w:t>Tel 070-12 34 567</w:t></w:r></w:p>",
            DocxStoryFixture.Paragraph("header", "811218-9876"));

        result.ScanText.ShouldBe("Tel 070-12 34 567\n811218-9876");
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxStoryEndingOutsideAParagraph_StillEndsItsLine()
    {
        // #1806's form: run content outside a paragraph, the one way a story's text ends without a line end.
        var result = ExtractStories(AnnaBody,
            DocxStoryFixture.Part("footer",
                "<w:ins w:id=\"1\" w:author=\"a\"><w:r><w:t>Tel 070-12 34 567</w:t></w:r></w:ins>"),
            DocxStoryFixture.Paragraph("footnote", "811218-9876"));

        result.AuxiliaryText.ShouldBe("Tel 070-12 34 567\n811218-9876");
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxMainTextAtTheCharacterCap_StillScansTheOtherStories()
    {
        var result = ExtractStories("<w:p><w:r><w:t>" + new string('A', 1_000_001) + "</w:t></w:r></w:p>",
            DocxStoryFixture.Paragraph("header", "811218-9876"));

        result.RawText.Length.ShouldBe(1_000_000);
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxHeaderWithTwoRelationships_IsReadOnce()
    {
        var docx = BuildDocxWithRelationships(
            AnnaBody,
            [("rId1", "header", "header1.xml"), ("rId2", "header", "header1.xml")],
            ("header", "header1.xml", DocxStoryFixture.Xml("header", "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>")));

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.AuxiliaryText.ShouldBe("811218-9876");
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxRelationshipsThatResolveToNoPart_SkipOnlyThemselves()
    {
        // A part the package does not hold, and an absolute target, which is no part at all.
        var docx = BuildDocxWithRelationships(
            AnnaBody,
            [("rId1", "header", "header9.xml"), ("rId2", "header", "https://example.se/header.xml"),
                ("rId3", "header", "header1.xml")],
            ("header", "header1.xml", DocxStoryFixture.Xml("header", "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>")));

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("Anna Andersson");
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxExternalStoryRelationship_IsNeverRead()
    {
        // The target names a part the package holds, which an external relationship must never reach.
        var relationships = RelationshipsXml(
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" " +
            "Target=\"header1.xml\" TargetMode=\"External\"/>");
        var docx = BuildDocxWithRelationshipsXml(AnnaBody, relationships,
            ("header", "header1.xml", DocxStoryFixture.Xml("header", "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>")));

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.AuxiliaryText.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_DocxRelationshipsOfOtherTypes_AreNeitherReadNorCounted()
    {
        // Every file Word writes relates its main part to parts that are no story; 64 images before a header.
        var images = Enumerable.Range(1, 64).ToArray();
        var docx = BuildDocxWithRelationships(
            AnnaBody,
            [.. images.Select(i => ("rIdI" + i, "image", "media/image" + i + ".png")), ("rIdH", "header", "header1.xml")],
            [.. images.Select(i => ("image", "media/image" + i + ".png", "PNG")),
                ("header", "header1.xml", DocxStoryFixture.Xml("header", "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>"))]);

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.AuxiliaryText.ShouldBe("811218-9876");
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxRelationshipPartPastTheByteBudget_IsReadAfterTheMainText_AndListsNothing()
    {
        // Comments of three-byte characters: 11.7 MB in the main part and 5.4 MB in the relationship part stay under
        // the reader's character ceiling and together pass the document's byte budget.
        var relationships = RelationshipsXml(
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" " +
            "Target=\"header1.xml\"/><!--" + new string('\u20AC', 1_800_000) + "-->");
        var docx = BuildDocxWithRelationshipsXml(AnnaBody + "<!--" + new string('\u20AC', 3_900_000) + "-->", relationships,
            ("header", "header1.xml", DocxStoryFixture.Xml("header", "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>")));

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("Anna Andersson");
        result.AuxiliaryText.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_DocxListingThatFailsAfterAStory_ListsNothing()
    {
        // The relationship part lists a header, then passes the reader's character ceiling.
        var relationships = RelationshipsXml(
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" " +
            "Target=\"header1.xml\"/><!--" + new string('x', 4_000_001) + "-->");
        var docx = BuildDocxWithRelationshipsXml(AnnaBody, relationships,
            ("header", "header1.xml", DocxStoryFixture.Xml("header", "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>")));

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("Anna Andersson");
        result.AuxiliaryText.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_DocxStoryRelationshipToTheMainPart_ReadsTheMainTextOnce()
    {
        var docx = BuildDocxWithRelationships(
            "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>", [("rId1", "header", "document.xml")]);

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("811218-9876");
        result.AuxiliaryText.ShouldBeEmpty();
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxRelationshipsTheReaderRejects_CostTheMainTextNothing()
    {
        var relationships = RelationshipsXml(
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>")
            .Replace("<Relationships ", "<!DOCTYPE Relationships [<!ENTITY x \"y\">]><Relationships ");
        var docx = BuildDocxWithRelationshipsXml("<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>", relationships);

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("811218-9876");
        FlagCount(result).ShouldBe(1);
    }

    [Theory]
    [InlineData(1024, 1)]
    [InlineData(1025, 0)]
    public void Extract_DocxStoryRelationshipPastTheRelationshipBound_IsNotRead(int position, int flags)
    {
        var links = Enumerable.Range(1, position - 1).Select(i => ("rIdL" + i, "hyperlink", "https://example.se/" + i));
        var docx = BuildDocxWithRelationships(
            AnnaBody,
            [.. links, ("rIdH", "header", "header1.xml")],
            ("header", "header1.xml", DocxStoryFixture.Xml("header", "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>")));

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("Anna Andersson");
        FlagCount(result).ShouldBe(flags);
    }

    [Fact]
    public void Extract_DocxWithTenThousandHeaders_ListsAtMostSixtyFour_AndKeepsTheMainText()
    {
        var headers = Enumerable.Range(1, 10_000).ToArray();
        var docx = BuildDocxWithRelationships(
            "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>",
            [.. headers.Select(i => ("rId" + i, "header", "header" + i + ".xml"))],
            [.. headers.Select(i => ("header", "header" + i + ".xml",
                DocxStoryFixture.Xml("header", "<w:p><w:r><w:t>Rubrik " + i + "</w:t></w:r></w:p>")))]);

        var result = _sut.Extract(docx, CvFileKind.Docx, CancellationToken.None);

        result.RawText.ShouldBe("811218-9876");
        result.AuxiliaryText.Split('\n').Length.ShouldBe(64);
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxWithAnEmptyMainStory_StaysEmptyAndKeepsTheOtherStories()
    {
        var result = ExtractStories("<w:p/>", DocxStoryFixture.Paragraph("header", "811218-9876"));

        result.Status.ShouldBe(CvExtractionStatus.Empty);
        result.RawText.ShouldBeEmpty();
        result.AuxiliaryText.ShouldBe("811218-9876");
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxStoryTheReaderRejects_AddsNothing_AndTheNextStoryIsRead()
    {
        // The hardened reader refuses a DTD here as it does in the main part; the footer after it is still read.
        const string body = "<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>";
        var rejected = "<!DOCTYPE w:hdr [<!ENTITY x \"y\">]>" +
            DocxStoryFixture.Xml("header", "<w:p><w:r><w:t>&x;</w:t></w:r></w:p>");

        var withIt = ExtractStories(body, ("header", rejected), DocxStoryFixture.Paragraph("footer", "Kontakt"));
        var without = ExtractStories(body);

        withIt.RawText.ShouldBe(without.RawText);
        withIt.AuxiliaryText.ShouldBe("Kontakt");
        FlagCount(withIt).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxPartsPastTheByteBudget_AddNothing_AndTheMainPartCountsTowardsIt()
    {
        // Each part stays under the reader's character ceiling; the main part and four headers together pass the
        // document's byte budget, so the fourth header is the one that does not fit.
        var filler = new string(' ', 3_900_000);
        var headers = Enumerable.Range(1, 4)
            .Select(i => DocxStoryFixture.Part("header", "<w:p><w:r><w:t>Rad " + i + "</w:t></w:r></w:p>" + filler))
            .ToArray();

        var result = ExtractStories("<w:p><w:r><w:t>811218-9876</w:t></w:r></w:p>" + filler, headers);

        result.RawText.ShouldBe("811218-9876");
        result.AuxiliaryText.Split('\n').ShouldBe(["Rad 1", "Rad 2", "Rad 3"], ignoreOrder: true);
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxWithMoreStoriesThanTheCap_ReadsSixtyFour()
    {
        var headers = Enumerable.Range(1, 65).Select(i => DocxStoryFixture.Paragraph("header", "Rubrik " + i)).ToArray();

        var result = ExtractStories(AnnaBody, headers);

        result.AuxiliaryText.Split('\n').Length.ShouldBe(64);
    }

    [Fact]
    public void Extract_DocxTextBoxInAlternateContentInAHeader_ReachesTheScan()
    {
        // Both branches are read today (#1801), so the oracle holds whether one or both are.
        var box = "<w:p><w:r><mc:AlternateContent>" +
            "<mc:Choice Requires=\"wps\">" + TextBox("drawing anchor", "811218-9876") + "</mc:Choice>" +
            "<mc:Fallback>" + TextBox("vml floating", "811218-9876") + "</mc:Fallback>" +
            "</mc:AlternateContent></w:r></w:p>";

        var result = ExtractStories(AnnaBody, DocxStoryFixture.Part("header", box));

        result.RawText.ShouldBe("Anna Andersson");
        result.ScanText.Split('\n').Where(line => line.Contains("811218")).ShouldAllBe(line => line == "811218-9876");
        var occurrences = result.ScanText.Split("811218-9876").Length - 1;
        occurrences.ShouldBeGreaterThanOrEqualTo(1);
        FlagCount(result).ShouldBe(occurrences);
    }

    // #1803 — tracked changes. RawText and AuxiliaryText read each story with every change accepted, a separator
    // standing where accepting removes a line end. The scan also reads each group a change touches as its runs are
    // written, accepted with nothing at a removed line end, and rejected.
    public static TheoryData<string> DeletionStories() => ["main", "header", "footer", "footnote", "endnote", "comment"];

    [Theory]
    [MemberData(nameof(DeletionStories))]
    public void Extract_DocxDeletedPersonnummer_ReachesTheScanButNotRawText(string story)
    {
        var deleted = Para(Del(DelText(Pnr)));

        var result = story == "main"
            ? ExtractStories(AnnaBody + deleted)
            : ExtractStories(AnnaBody, DocxStoryFixture.Part(story, deleted));

        result.RawText.ShouldBe("Anna Andersson");
        result.AuxiliaryText.ShouldBeEmpty();
        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> PartlyDeletedShapes() =>
        ["kept head", "kept tail", "two runs in one deletion", "two deletions", "a proofing mark between",
            "a bookmark between", "a comment range between"];

    [Theory]
    [MemberData(nameof(PartlyDeletedShapes))]
    public void Extract_DocxPartlyDeletedPersonnummer_ReachesTheScanWhole(string shape)
    {
        var (body, rawText) = shape switch
        {
            "kept head" => (Para(Kept("811218-"), Del(DelText("9876"))), "811218-"),
            "kept tail" => (Para(Del(DelText("811218-")), Kept("9876")), "9876"),
            "two runs in one deletion" => (Para(Del(DelText("811218-"), DelText("9876"))), ""),
            "two deletions" => (Para(Del(DelText("811218-")), Del(DelText("9876"))), ""),
            "a proofing mark between" =>
                (Para(Del(DelText("811218")), "<w:proofErr w:type=\"spellStart\"/>", Del(DelText("-9876"))), ""),
            "a bookmark between" =>
                (Para(Del(DelText("811218")), "<w:bookmarkStart w:id=\"0\" w:name=\"b\"/><w:bookmarkEnd w:id=\"0\"/>",
                    Del(DelText("-9876"))), ""),
            "a comment range between" =>
                (Para(Del(DelText("811218")), "<w:commentRangeStart w:id=\"0\"/><w:commentRangeEnd w:id=\"0\"/>",
                    Del(DelText("-9876"))), ""),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        var result = ExtractStories(body);

        result.RawText.ShouldBe(rawText);
        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> ReplacementShapes() => ["whole number", "last four by digits", "last four by letters"];

    [Theory]
    [MemberData(nameof(ReplacementShapes))]
    public void Extract_DocxPersonnummerReplacedByAnInsertion_ReachesTheScan(string shape)
    {
        var (body, rawText) = shape switch
        {
            "whole number" => (Para(Del(DelText(Pnr)), Ins(Kept("[borttaget]"))), "[borttaget]"),
            "last four by digits" => (Para(Kept("811218-"), Del(DelText("9876")), Ins(Kept("0000"))), "811218-0000"),
            "last four by letters" => (Para(Kept("811218-"), Del(DelText("9876")), Ins(Kept("XXXX"))), "811218-XXXX"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        var result = ExtractStories(body);

        result.RawText.ShouldBe(rawText);
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxLineDeletedAfterADeletedMark_ReachesTheScanOnItsOwnLine()
    {
        // Three digits end the line before, so a rejected reading that joined the two lines would find no number.
        var result = ExtractStories(MarkedPara(DeletedMark, Kept("Tel 070-123 45 678")) + Para(Del(DelText(Pnr))));

        result.RawText.ShouldBe("Tel 070-123 45 678");
        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> RemovedBoundaries() =>
        ["deleted mark", "deleted mark with an end tag", "moved mark", "deleted break", "deleted carriage return"];

    [Theory]
    [MemberData(nameof(RemovedBoundaries))]
    public void Extract_DocxBoundaryTheAcceptanceRemoves_IsASeparatorInRawText(string shape)
    {
        var body = shape switch
        {
            "deleted mark" => MarkedPara(DeletedMark, Kept("811218-")) + Para(Kept("9876")),
            "deleted mark with an end tag" => MarkedPara(DeletedMarkWithEndTag, Kept("811218-")) + Para(Kept("9876")),
            "moved mark" => MarkedPara(MovedMark, Kept("811218-")) + Para(Kept("9876")),
            "deleted break" => Para(Kept("811218-"), Del("<w:r><w:br/></w:r>"), Kept("9876")),
            "deleted carriage return" => Para(Kept("811218-"), Del("<w:r><w:cr/></w:r>"), Kept("9876")),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        var result = ExtractStories(body);

        // RawText and the reading joined with nothing each carry the number once.
        result.RawText.ShouldBe("811218- 9876");
        FlagCount(result).ShouldBe(2);
    }

    public static TheoryData<string> DigitBesideADeletedMark() => ["before", "after"];

    [Theory]
    [MemberData(nameof(DigitBesideADeletedMark))]
    public void Extract_DocxDeletedMarkBesideADigit_KeepsTheNumberApartInRawText(string side)
    {
        var (body, rawText) = side switch
        {
            "before" => (MarkedPara(DeletedMark, Kept("Ref 1")) + Para(Kept(Pnr)), "Ref 1 811218-9876"),
            "after" => (MarkedPara(DeletedMark, Kept(Pnr)) + Para(Kept("1 Ref")), "811218-9876 1 Ref"),
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };

        var result = ExtractStories(body);

        result.RawText.ShouldBe(rawText);
        FlagCount(result).ShouldBe(2);
    }

    [Fact]
    public void Extract_DocxMovedPersonnummer_IsReadOnceInRawText()
    {
        var result = ExtractStories(Para(MovedFrom(Kept(Pnr))) + Para(MovedTo(Kept(Pnr))));

        result.RawText.ShouldBe("811218-9876");
        FlagCount(result).ShouldBe(2);
    }

    [Fact]
    public void Extract_DocxMovedTextBetweenDigits_IsASeparatorInRawText()
    {
        var result = ExtractStories(Para(Kept("12"), MovedFrom(Kept("x")), Kept(Pnr)));

        result.RawText.ShouldBe("12 811218-9876");
    }

    [Fact]
    public void Extract_DocxInsertedMarkInsideAPersonnummer_JoinsInTheScan()
    {
        var result = ExtractStories(MarkedPara(InsertedMark, Kept("811218-")) + Para(Kept("9876")));

        result.RawText.ShouldBe("811218-\n9876");
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxInsertedMarkBeforeTabStops_JoinsTheRejectedReading()
    {
        // The rejected reading continues the line into the next paragraph's properties, where a tab stop is no tab.
        var result = ExtractStories(MarkedPara(InsertedMark, Kept("8112")) + TabStopPara(Kept("18-9876")));

        result.RawText.ShouldBe("8112\n18-9876");
        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> DeletedMarkInsideTheBirthDate() => ["plain", "tab stops after it"];

    [Theory]
    [MemberData(nameof(DeletedMarkInsideTheBirthDate))]
    public void Extract_DocxDeletedMarkInsideTheBirthDate_JoinsInTheScan(string shape)
    {
        var next = shape == "plain" ? Para(Kept("18-9876")) : TabStopPara(Kept("18-9876"));

        var result = ExtractStories(MarkedPara(DeletedMark, Kept("8112")) + next);

        // The separator keeps RawText apart, and the gap bridge needs six or eight digits before it.
        result.RawText.ShouldBe("8112 18-9876");
        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> DeletedContentShapes() =>
        ["text in a deletion", "deleted text outside a deletion", "a deletion inside an insertion", "a deleted text box"];

    [Theory]
    [MemberData(nameof(DeletedContentShapes))]
    public void Extract_DocxDeletedContent_LeavesRawTextAndReachesTheScan(string shape)
    {
        var body = shape switch
        {
            "text in a deletion" => Para(Del(Kept(Pnr))),
            "deleted text outside a deletion" => Para(DelText(Pnr)),
            "a deletion inside an insertion" => Para(Ins(Del(DelText(Pnr)))),
            "a deleted text box" => Para(Del(TextBoxRun(Para(Kept(Pnr))))),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        var result = ExtractStories(body);

        result.RawText.ShouldBeEmpty();
        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> DeletedTextContainers() => ["a text box", "a deleted table row"];

    [Theory]
    [MemberData(nameof(DeletedTextContainers))]
    public void Extract_DocxDeletedTextInAContainer_ReachesTheScan(string container)
    {
        var deleted = Para(Del(DelText(Pnr)));
        var body = container switch
        {
            "a text box" => Para(TextBoxRun(deleted)),
            "a deleted table row" => "<w:tbl><w:tr><w:trPr>" + DeletedMark + "</w:trPr><w:tc>" + deleted +
                "</w:tc></w:tr></w:tbl>",
            _ => throw new ArgumentOutOfRangeException(nameof(container), container, null),
        };

        FlagCount(ExtractStories(body)).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxDeletedLineAfterADigitRun_StartsItsOwnLineInTheScan()
    {
        var result = ExtractStories(Para(Kept("Tel 070-12 34 567")) + Para(Del(DelText(Pnr))));

        result.ScanText.ShouldBe("Tel 070-12 34 567\n811218-9876");
        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> SecondStoryWithAChange() => ["main then header", "two headers"];

    [Theory]
    [MemberData(nameof(SecondStoryWithAChange))]
    public void Extract_DocxChangedGroupsOfTwoStories_AreSeparateLinesInTheScan(string stories)
    {
        // The first group's rejected reading ends in digits, the second's starts with the number.
        var endsInDigits = Para(Del(DelText("x")), Kept("Tel 070-12 34 567"));
        var number = Para(Del(DelText(Pnr)));

        var result = stories == "main then header"
            ? ExtractStories(endsInDigits, DocxStoryFixture.Part("header", number))
            : ExtractStories(AnnaBody, DocxStoryFixture.Part("header", endsInDigits), DocxStoryFixture.Part("header", number));

        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxMainStoryOnlyDeleted_IsEmptyAndReachesTheScan()
    {
        var result = ExtractStories(Para(Del(DelText(Pnr))));

        result.Status.ShouldBe(CvExtractionStatus.Empty);
        result.RawText.ShouldBeEmpty();
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxMainTextAtTheCharacterCap_StillScansAHeadersDeletedPersonnummer()
    {
        var result = ExtractStories(Para(Kept(new string('A', 1_000_001))),
            DocxStoryFixture.Part("header", Para(Del(DelText(Pnr)))));

        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxKeptPersonnummerInAGroupEveryReadingChanges_IsCountedOncePerReading()
    {
        var result = ExtractStories(
            MarkedPara(DeletedMark, Kept("811218-9876 a"), Del(DelText("b")), Ins(Kept("c"))) + Para(Kept("d")));

        result.RawText.ShouldBe("811218-9876 ac d");
        FlagCount(result).ShouldBe(4);
    }

    [Fact]
    public void Extract_DocxKeptPersonnummerBesideADeletedLetter_IsCountedByTheRejectedReadingToo()
    {
        var result = ExtractStories(Para(Kept("811218-9876 Utveckl"), Del(DelText("k")), Kept("are")));

        result.RawText.ShouldBe("811218-9876 Utvecklare");
        FlagCount(result).ShouldBe(2);
    }

    public static TheoryData<string> FragmentsKeptApart() => ["kept text between", "a paragraph end between"];

    [Theory]
    [MemberData(nameof(FragmentsKeptApart))]
    public void Extract_DocxDeletedFragmentsWithAKeptBoundaryBetween_AreNotJoined(string boundary)
    {
        var body = boundary == "kept text between"
            ? Para(Del(DelText("811218-")), Kept("x"), Del(DelText("9876")))
            : Para(Del(DelText("811218-"))) + Para(Del(DelText("9876")));

        FlagCount(ExtractStories(body)).ShouldBe(0);
    }

    public static TheoryData<string> ChangeOnTheNextLine() => ["body", "footer ending outside a paragraph"];

    [Theory]
    [MemberData(nameof(ChangeOnTheNextLine))]
    public void Extract_DocxKeptPersonnummerBeforeAChangedLine_IsCountedOnce(string where)
    {
        var result = where == "body"
            ? ExtractStories(Para(Kept(Pnr)) + Para(Del(DelText("x"))))
            : ExtractStories(AnnaBody, DocxStoryFixture.Part("footer", Para(Kept(Pnr)) + Ins(Kept("x"))));

        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> RowMarkers() => [DeletedMark, DeletedMarkWithEndTag];

    [Theory]
    [MemberData(nameof(RowMarkers))]
    public void Extract_DocxDeletedRowMarker_LeavesTheTextAfterItInRawText(string marker)
    {
        var result = ExtractStories("<w:tbl><w:tr><w:trPr>" + marker + "</w:trPr><w:tc>" + Para(Kept(Pnr)) +
            "</w:tc></w:tr></w:tbl>" + Para(Kept("Rad två")));

        result.RawText.ShouldBe("811218-9876\nRad två");
    }

    [Fact]
    public void Extract_DocxDeletedTextPastTheCharacterCap_KeepsTheTextAfterItInRawText()
    {
        var result = ExtractStories(Para(Del(DelText(new string('x', 1_000_001))), Kept(Pnr)));

        result.RawText.ShouldBe("811218-9876");
    }

    [Fact]
    public void Extract_DocxMovedAndInsertedSpacesAroundAPersonnummer_KeepItApart()
    {
        var result = ExtractStories(Para(Kept("1"), MovedFrom(Kept(" ")), Kept(Pnr), Ins(Kept(" ")), Kept("3")));

        result.RawText.ShouldBe("1 811218-9876 3");
        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> MovedTextNoOtherReadingCarries() => ["beside a deleted digit", "head moved, tail inserted"];

    [Theory]
    [MemberData(nameof(MovedTextNoOtherReadingCarries))]
    public void Extract_DocxMovedTextOnlyTheWrittenReadingCarries_ReachesTheScan(string shape)
    {
        var body = shape == "beside a deleted digit"
            ? Para(MovedFrom(Kept(Pnr)), Del(DelText("5")))
            : Para(MovedFrom(Kept("811218-")), Ins(Kept("9876")));

        FlagCount(ExtractStories(body)).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxMainStoryDeletionPastItsRoom_LeavesTheOtherStoriesRoom()
    {
        var result = ExtractStories(Para(Del(DelText(new string('x', 1_000_001)))),
            DocxStoryFixture.Part("header", Para(Del(DelText(Pnr)))));

        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxMovedPersonnummerAfterADeletionPastItsRoom_ReachesTheScan()
    {
        // The rejected reading of the first group fills its room; the written reading of the second has its own.
        var result = ExtractStories(Para(Del(DelText(new string('x', 1_000_001)))) + Para(MovedFrom(Kept(Pnr))));

        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxDeletionAfterTheLastParagraph_ReachesTheScan()
    {
        var result = ExtractStories(AnnaBody + Del(DelText(Pnr)));

        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxDeletedTextBoxsSecondParagraph_ReachesTheScan()
    {
        // The first paragraph's end is a line end in every reading, so the second starts inside the open deletion.
        var result = ExtractStories(Para(Del(TextBoxRun(Para(Kept("a")), Para(Kept(Pnr))))));

        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxMovedPersonnummerBeforeTheCharacterCap_ReachesTheScan()
    {
        var result = ExtractStories(Para(MovedFrom(Kept(Pnr)), Kept(new string('A', 1_000_000))));

        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxHeaderPastTheByteBudget_AddsNoneOfItsChangedText()
    {
        // The fourth header's changed paragraph is read before the budget runs out in its filler.
        var filler = new string(' ', 3_900_000);
        var headers = Enumerable.Range(1, 3)
            .Select(i => DocxStoryFixture.Part("header", Para(Kept("Rad " + i)) + filler))
            .Append(DocxStoryFixture.Part("header", Para(Del(DelText(Pnr))) + filler))
            .ToArray();

        var result = ExtractStories(Para(Kept(Pnr)) + filler, headers);

        FlagCount(result).ShouldBe(1);
    }

    public static TheoryData<string> DocumentsWithoutTrackedChanges() => ["a header's number", "a text box", "breaks and tabs"];

    [Theory]
    [MemberData(nameof(DocumentsWithoutTrackedChanges))]
    public void Extract_DocxWithoutTrackedChanges_HasNoRevisionText(string shape)
    {
        var result = shape switch
        {
            "a header's number" => ExtractStories(AnnaBody, DocxStoryFixture.Paragraph("header", Pnr)),
            "a text box" => ExtractStories(Para(TextBoxRun(Para(Kept(Pnr))))),
            "breaks and tabs" => ExtractStories(Para(Kept("Anna"), "<w:r><w:br/><w:tab/></w:r>", Kept(Pnr))),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        result.RevisionText.ShouldBeEmpty();
        FlagCount(result).ShouldBe(1);
    }

    [Fact]
    public void Extract_DocxWithOnlyFormattingChanges_HasNoRevisionText()
    {
        var result = ExtractStories(Para(
            "<w:pPr><w:pPrChange w:id=\"7\" w:author=\"a\"><w:pPr/></w:pPrChange></w:pPr>",
            "<w:r><w:rPr><w:b/><w:rPrChange w:id=\"8\" w:author=\"a\"><w:rPr/></w:rPrChange></w:rPr><w:t>811218-9876</w:t></w:r>"));

        result.RawText.ShouldBe("811218-9876");
        result.RevisionText.ShouldBeEmpty();
    }

    public static TheoryData<string> RejectedReadingJoins() => ["kept head, deleted tail", "inserted mark before tab stops"];

    [Theory]
    [MemberData(nameof(RejectedReadingJoins))]
    public void Extract_DocxChangeOnlyTheRejectedReadingUndoes_IsItsRevisionText(string shape)
    {
        var body = shape == "kept head, deleted tail"
            ? Para(Kept("811218-"), Del(DelText("9876")))
            : MarkedPara(InsertedMark, Kept("8112")) + TabStopPara(Kept("18-9876"));

        ExtractStories(body).RevisionText.ShouldBe("811218-9876");
    }

    [Fact]
    public void Extract_DocxHeaderPastTheByteBudget_RollsBackItsRevisionText()
    {
        var filler = new string(' ', 3_900_000);
        var headers = Enumerable.Range(1, 4)
            .Select(i => DocxStoryFixture.Part("header", Para(Del(DelText("Rad " + i))) + filler))
            .ToArray();

        var result = ExtractStories(Para(Kept(Pnr)) + filler, headers);

        result.RevisionText.Split('\n').ShouldBe(["Rad 1", "Rad 2", "Rad 3"], ignoreOrder: true);
    }

    [Fact]
    public void Extract_DocxDeletionLongerThanARoom_IsCutAtTheRoom()
    {
        var result = ExtractStories(Para(Del(DelText(new string('x', 2_000_000)))));

        result.RevisionText.Length.ShouldBe(1_000_000);
    }

    private static bool IsInline(string form) => form is "vml inline" or "drawing inline";

    // A text box holding one paragraph, as the run content of the paragraph that anchors it.
    private static string TextBox(string form, string text)
    {
        var content = "<w:txbxContent><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:txbxContent>";
        return form switch
        {
            "vml floating" => "<w:pict><v:shape style=\"position:absolute;width:100pt;height:50pt\"><v:textbox>" +
                content + "</v:textbox></v:shape></w:pict>",
            "vml inline" => "<w:pict><v:shape style=\"width:100pt;height:50pt\"><v:textbox>" +
                content + "</v:textbox></v:shape></w:pict>",
            "drawing anchor" => "<w:drawing><wp:anchor>" + Graphic("<wps:wsp><wps:txbx>" + content + "</wps:txbx></wps:wsp>") +
                "</wp:anchor></w:drawing>",
            "drawing inline" => "<w:drawing><wp:inline>" + Graphic("<wps:wsp><wps:txbx>" + content + "</wps:txbx></wps:wsp>") +
                "</wp:inline></w:drawing>",
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };
    }

    // An image, as the run content of the paragraph that holds it.
    private static string ObjectWithoutText(string form) => form switch
    {
        "drawing inline" => "<w:drawing><wp:inline>" + Graphic("<pic:pic/>") + "</wp:inline></w:drawing>",
        "drawing anchor" => "<w:drawing><wp:anchor>" + Graphic("<pic:pic/>") + "</wp:anchor></w:drawing>",
        "vml inline" => "<w:pict><v:shape style=\"width:10pt;height:10pt\"><v:imagedata/></v:shape></w:pict>",
        "vml floating" => "<w:pict><v:shape style=\"position:absolute;width:10pt;height:10pt\"><v:imagedata/></v:shape></w:pict>",
        // Word also writes mso-position-horizontal:absolute, which positions nothing on its own.
        "vml mso-position only" =>
            "<w:pict><v:shape style=\"mso-position-horizontal:absolute;width:10pt;height:10pt\"><v:imagedata/></v:shape></w:pict>",
        "vml shapetype first" => "<w:pict><v:shapetype/><v:shape style=\"position:absolute;width:10pt;height:10pt\">" +
            "<v:imagedata/></v:shape></w:pict>",
        "object" => "<w:object><v:shape style=\"width:10pt;height:10pt\"><v:imagedata/></v:shape><o:OLEObject/></w:object>",
        _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
    };

    private static string Graphic(string data) =>
        "<a:graphic><a:graphicData uri=\"urn:example\">" + data + "</a:graphicData></a:graphic>";

    private string ExtractXml(string body) =>
        _sut.Extract(BuildMinimalOpcWithDocumentXml(DocumentXml(body)), CvFileKind.Docx, CancellationToken.None).RawText;

    private static readonly XNamespace WordMain = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string MainDocumentXml(byte[] docx)
    {
        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }

    private static OpenXmlElement RunElement(string name) => name switch
    {
        "br" => new Break(),
        "br page" => new Break { Type = BreakValues.Page },
        "br column" => new Break { Type = BreakValues.Column },
        "cr" => new CarriageReturn(),
        "tab" => new TabChar(),
        "ptab" => new PositionalTab
        {
            Alignment = AbsolutePositionTabAlignmentValues.Right,
            RelativeTo = AbsolutePositionTabPositioningBaseValues.Margin,
            Leader = AbsolutePositionTabLeaderCharValues.None,
        },
        "sym" => new SymbolChar { Font = "Wingdings", Char = "F028" },
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    // The import's flag path: ImportResumeCommandHandler scans ScanText. The profile it passes there is pinned by
    // PersonnummerGapProfileCallSiteTests.Every_production_call_site_passes_the_profile_its_text_kind_requires.
    private static int FlagCount(CvExtractionResult result) => FlagCount(result.ScanText);

    private static int FlagCount(string scanText) =>
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(
            scanText, PersonnummerGapProfile.ExtractedDocumentText)).Count;

    private CvExtractionResult ExtractStories(string bodyXml, params (string Story, string PartXml)[] parts) =>
        _sut.Extract(DocxStoryFixture.Build(bodyXml, parts), CvFileKind.Docx, CancellationToken.None);

    private static byte[] BuildDocxFromParagraphs(params Paragraph[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
            stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            // Fully qualified: `Document` is ambiguous once QuestPDF.Fluent is in scope (same
            // resolution PdfPigCvLayoutAnalyzerTests:99 uses).
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body(paragraphs));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static string DocumentXml(string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
        "xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
        "<w:body>" + body + "</w:body></w:document>";

    // A package whose main part's relationships are written by hand: the SDK refuses two relationships to one part,
    // and a relationship to a part the package does not hold.
    private static byte[] BuildDocxWithRelationships(
        string bodyXml, (string Id, string Kind, string Target)[] relationships, params (string Kind, string Target, string Xml)[] parts) =>
        BuildDocxWithRelationshipsXml(bodyXml,
            RelationshipsXml(string.Concat(relationships.Select(r =>
                "<Relationship Id=\"" + r.Id + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/" +
                r.Kind + "\" Target=\"" + r.Target + "\"" + (r.Kind == "hyperlink" ? " TargetMode=\"External\"" : "") + "/>"))),
            parts);

    private static string RelationshipsXml(string elements) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + elements + "</Relationships>";

    private static byte[] BuildDocxWithRelationshipsXml(
        string bodyXml, string documentRelationshipsXml, params (string Kind, string Target, string Xml)[] parts)
    {
        var overrides = string.Concat(parts.Select(part =>
            "<Override PartName=\"/word/" + part.Target + "\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml." +
            part.Kind + "+xml\"/>"));
        var contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            overrides + "</Types>";
        const string rootRelationships =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" " +
            "Target=\"word/document.xml\"/></Relationships>";

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", contentTypes);
            Write(archive, "_rels/.rels", rootRelationships);
            Write(archive, "word/_rels/document.xml.rels", documentRelationshipsXml);
            Write(archive, "word/document.xml", DocumentXml(bodyXml));
            foreach (var (_, target, xml) in parts)
                Write(archive, "word/" + target, xml);
        }

        return ms.ToArray();

        static void Write(ZipArchive archive, string name, string content)
        {
            using var stream = archive.CreateEntry(name).Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    // Builds a minimal, VALID OPC/DOCX package (the three required parts) with a
    // caller-supplied word/document.xml — so a crafted document.xml (e.g. a DTD payload)
    // is reached through the real WordprocessingDocument.Open main-part resolution + the
    // hardened XmlReader, exactly the production path.
    private static byte[] BuildMinimalOpcWithDocumentXml(string documentXml)
    {
        const string contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>";
        const string rels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" " +
            "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" " +
            "Target=\"word/document.xml\"/></Relationships>";

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", contentTypes);
            WriteEntry(archive, "_rels/.rels", rels);
            WriteEntry(archive, "word/document.xml", documentXml);
        }

        return ms.ToArray();

        static void WriteEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
