namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// Deterministic PDF/DOCX → plain-text extraction (F4-8, NO AI/LLM). The PdfPig and
/// OpenXml SDKs live behind THIS port and nowhere else (Clean Architecture — parity
/// <c>ITextAnalyzer</c>). Synchronous and in-memory: the underlying libraries expose
/// synchronous APIs over a byte buffer, so there is no I/O to await (CLAUDE.md §3.5);
/// the DoS input-size cap and any thread-offload decision belong in the handler, not
/// the port.
/// </summary>
public interface ICvTextExtractor
{
    /// <summary>
    /// Extracts raw text from <paramref name="file"/> as the given
    /// <paramref name="kind"/>. Never throws on a malformed/corrupt/oversized file —
    /// it returns <c>CvExtractionStatus.Empty</c>/<c>NoTextLayer</c> so the caller can
    /// route to the manual-fallback path (OQ5). Deterministic: identical bytes yield
    /// identical text.
    /// </summary>
    CvExtractionResult Extract(ReadOnlyMemory<byte> file, CvFileKind kind);
}
