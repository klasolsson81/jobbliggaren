using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

// Fas 4 STEG 8 (F4-8, NO AI/LLM) — PdfPigOpenXmlCvTextExtractor is the format→text port
// impl (PdfPig/OpenXml confined behind it; internal, visible via InternalsVisibleTo).
// Contract: fail-SOFT — a corrupt/empty file NEVER throws, it returns
// Empty/NoTextLayer so the handler routes to manual fallback (OQ5). The DOCX path is
// exercised with a synthesized in-memory WordprocessingDocument (incl. åäö); the PDF
// path's happy case is exercised by the integration/manual path — here we only assert
// the malformed-PDF fail-soft contract (a faked valid PDF would not be a real test).
public class PdfPigOpenXmlCvTextExtractorTests
{
    private readonly PdfPigOpenXmlCvTextExtractor _sut = new();

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
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    [Fact]
    public void Extract_SynthesizedDocx_StatusExtracted_RawTextContainsParagraphs()
    {
        var bytes = BuildDocx("Anna Andersson", "Backend-utvecklare på Acme AB");

        var result = _sut.Extract(bytes, CvFileKind.Docx);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        result.RawText.ShouldContain("Anna Andersson");
        result.RawText.ShouldContain("Backend-utvecklare på Acme AB");
    }

    [Fact]
    public void Extract_DocxWithSwedishCharacters_PreservesAaO()
    {
        // åäö must survive extraction/serialization (CLAUDE.md §10 — UTF-8 everywhere).
        var bytes = BuildDocx("Förskollärare", "Erfarenhet av kärnverksamhet på äldreboende");

        var result = _sut.Extract(bytes, CvFileKind.Docx);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        result.RawText.ShouldContain("Förskollärare");
        result.RawText.ShouldContain("äldreboende");
    }

    [Fact]
    public void Extract_EmptyBytesAsDocx_StatusEmpty_NoThrow()
    {
        var result = _sut.Extract(ReadOnlyMemory<byte>.Empty, CvFileKind.Docx);

        result.Status.ShouldBe(CvExtractionStatus.Empty);
        result.RawText.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_GarbageBytesAsDocx_StatusEmpty_NeverThrows()
    {
        byte[] garbage = [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x01];

        CvExtractionResult result = default!;
        Should.NotThrow(() => result = _sut.Extract(garbage, CvFileKind.Docx));

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
        Should.NotThrow(() => result = _sut.Extract(bomb, CvFileKind.Docx));

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
        Should.NotThrow(() => result = _sut.Extract(bytes, CvFileKind.Docx));

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
        Should.NotThrow(() => result = _sut.Extract(notADocx, CvFileKind.Docx));

        result.Status.ShouldBe(CvExtractionStatus.Empty);
        result.RawText.ShouldBeEmpty();
    }

    [Fact]
    public void Extract_NormalSizedDocx_NotRejectedByZipBombGuard()
    {
        // A real CV is well under the ceiling — the guard must not produce false rejections.
        var bytes = BuildDocx("Anna Andersson", "Systemutvecklare", "C#, PostgreSQL, Docker");

        var result = _sut.Extract(bytes, CvFileKind.Docx);

        result.Status.ShouldBe(CvExtractionStatus.Extracted);
        result.RawText.ShouldContain("Systemutvecklare");
    }

    [Fact]
    public void Extract_MalformedBytesAsPdf_StatusEmpty_NeverThrows()
    {
        // NOTE: synthesizing a VALID PDF in-memory is impractical, so the happy DOCX
        // path stands in for the structured-extraction contract above. Real-PDF text
        // extraction is exercised by the integration/manual path (architect advisory).
        // Here we pin only the fail-soft contract for a malformed PDF: never throws.
        byte[] malformed = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x00, 0xDE, 0xAD];

        CvExtractionResult result = default!;
        Should.NotThrow(() => result = _sut.Extract(malformed, CvFileKind.Pdf));

        result.Status.ShouldBe(CvExtractionStatus.Empty);
        result.RawText.ShouldBeEmpty();
    }
}
