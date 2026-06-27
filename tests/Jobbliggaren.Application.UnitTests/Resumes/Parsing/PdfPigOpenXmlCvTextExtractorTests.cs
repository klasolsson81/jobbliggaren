using System.IO.Compression;
using System.Text;
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

    [Fact]
    public void Extract_MalformedBytesAsPdf_StatusEmpty_NeverThrows()
    {
        // NOTE: synthesizing a VALID PDF in-memory is impractical, so the happy DOCX
        // path stands in for the structured-extraction contract above. Real-PDF text
        // extraction is exercised by the integration/manual path (architect advisory).
        // Here we pin only the fail-soft contract for a malformed PDF: never throws.
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
