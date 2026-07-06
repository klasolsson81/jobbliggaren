using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// Deterministic PDF page-geometry analysis (Fas 4b PR-6b, ADR 0093 §D4, NO AI/LLM). PdfPig
/// lives behind THIS port and nowhere else (Clean Architecture — parity
/// <see cref="ICvTextExtractor"/>); no PdfPig type crosses the Application boundary. Reads
/// ONLY the non-PII structural facts the geometry criteria need — page count and the tightest
/// page margin — never CV text (that is <see cref="ICvTextExtractor"/>'s job). Synchronous and
/// in-memory over a byte buffer (the DoS input-size cap belongs in the handler; the
/// <see cref="System.Threading.CancellationToken"/> is honoured cooperatively, checked between
/// pages). Separate from the text extractor by design (CTO-bind PR-6 D-E): geometry and text
/// are two concerns, and a second cheap parse of a &lt;10 MiB import is not a hot path.
/// </summary>
public interface ICvLayoutAnalyzer
{
    /// <summary>
    /// Reads the layout metrics of <paramref name="file"/> as the given
    /// <paramref name="kind"/>. Never throws on a malformed/corrupt/oversized/non-PDF file —
    /// it returns <see cref="LayoutGeometryStatus.Failed"/>/<see cref="LayoutGeometryStatus.NotApplicable"/>
    /// with the file size still populated, so the caller persists honest metrics and the review
    /// verdicts NotAssessed on absent geometry. A DOCX is <see cref="LayoutGeometryStatus.NotApplicable"/>
    /// (CTO-bind D10 — no server-side conversion). Deterministic: identical bytes yield identical
    /// metrics. Honours <paramref name="cancellationToken"/> cooperatively; a cancellation
    /// surfaces as <see cref="System.OperationCanceledException"/> (NOT the fail-soft path).
    /// </summary>
    CvLayoutMetrics Analyze(
        ReadOnlyMemory<byte> file, CvFileKind kind, CancellationToken cancellationToken);
}
