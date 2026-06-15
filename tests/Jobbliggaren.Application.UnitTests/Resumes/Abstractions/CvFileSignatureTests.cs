using Jobbliggaren.Application.Resumes.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Abstractions;

// Fas 4 STEG 8 (F4-8, security-auditor scope) — CvFileSignature resolves the upload
// kind from MAGIC BYTES (authoritative) + declared content-type (defence-in-depth).
// A renamed/non-PDF/DOCX payload is rejected regardless of a spoofable MIME; a
// declared content-type that names the OTHER supported format is a mismatch → reject;
// an empty/octet-stream content-type is tolerated (the magic bytes decide).
//
// SPEC-DRIVEN.
public class CvFileSignatureTests
{
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31]; // "%PDF-1"
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00]; // "PK\x03\x04…"

    [Fact]
    public void TryResolve_PdfMagicAndPdfContentType_ResolvesPdf()
    {
        var ok = CvFileSignature.TryResolve(
            CvFileSignature.PdfContentType, PdfMagic, out var kind);

        ok.ShouldBeTrue();
        kind.ShouldBe(CvFileKind.Pdf);
    }

    [Fact]
    public void TryResolve_ZipMagicAndDocxContentType_ResolvesDocx()
    {
        var ok = CvFileSignature.TryResolve(
            CvFileSignature.DocxContentType, ZipMagic, out var kind);

        ok.ShouldBeTrue();
        kind.ShouldBe(CvFileKind.Docx);
    }

    [Fact]
    public void TryResolve_PdfMagicButDeclaredDocxContentType_RejectsAsMismatch()
    {
        var ok = CvFileSignature.TryResolve(
            CvFileSignature.DocxContentType, PdfMagic, out _);

        ok.ShouldBeFalse();
    }

    [Fact]
    public void TryResolve_ZipMagicButDeclaredPdfContentType_RejectsAsMismatch()
    {
        var ok = CvFileSignature.TryResolve(
            CvFileSignature.PdfContentType, ZipMagic, out _);

        ok.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("application/octet-stream")]
    [InlineData("application/pdf; charset=binary")] // parameters stripped
    public void TryResolve_PdfMagicWithTolerantContentType_ResolvesPdf(string? contentType)
    {
        var ok = CvFileSignature.TryResolve(contentType, PdfMagic, out var kind);

        ok.ShouldBeTrue();
        kind.ShouldBe(CvFileKind.Pdf);
    }

    [Fact]
    public void TryResolve_RandomBytes_ReturnsFalse()
    {
        byte[] random = [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11];

        var ok = CvFileSignature.TryResolve(CvFileSignature.PdfContentType, random, out _);

        ok.ShouldBeFalse();
    }

    [Fact]
    public void TryResolve_EmptyBytes_ReturnsFalse()
    {
        var ok = CvFileSignature.TryResolve(CvFileSignature.PdfContentType, [], out _);

        ok.ShouldBeFalse();
    }
}
