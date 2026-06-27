namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// Deterministic PDF/DOCX → plain-text extraction (F4-8, NO AI/LLM). The PdfPig and
/// OpenXml SDKs live behind THIS port and nowhere else (Clean Architecture — parity
/// <c>ITextAnalyzer</c>). Synchronous and in-memory: the underlying libraries expose
/// synchronous APIs over a byte buffer, so there is no I/O to await (CLAUDE.md §3.5);
/// the DoS input-size cap and any thread-offload decision belong in the handler, not
/// the port. The <see cref="CancellationToken"/> is honoured COOPERATIVELY (the method
/// stays synchronous — it is checked between work units, not awaited), so a pathological
/// input is interruptible (#272 SEC-2, CLAUDE.md §3 end-to-end cancellation).
/// </summary>
public interface ICvTextExtractor
{
    /// <summary>
    /// Extracts raw text from <paramref name="file"/> as the given
    /// <paramref name="kind"/>. Never throws on a malformed/corrupt/oversized file —
    /// it returns <c>CvExtractionStatus.Empty</c>/<c>NoTextLayer</c> so the caller can
    /// route to the manual-fallback path (OQ5). Deterministic: identical bytes yield
    /// identical text. Honours <paramref name="cancellationToken"/> cooperatively; a
    /// cancellation surfaces as <see cref="OperationCanceledException"/> (NOT the
    /// fail-soft <c>Empty</c> path — a timeout must not masquerade as an unparseable file).
    /// </summary>
    CvExtractionResult Extract(
        ReadOnlyMemory<byte> file, CvFileKind kind, CancellationToken cancellationToken);
}
