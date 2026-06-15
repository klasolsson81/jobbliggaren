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
