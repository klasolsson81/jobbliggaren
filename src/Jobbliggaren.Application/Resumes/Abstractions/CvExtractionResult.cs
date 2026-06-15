namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// The extraction-level status of a CV text extraction (F4-8). Distinguishes "we got
/// text" from the two manual-fallback triggers a deterministic extractor can detect
/// without segmentation (OQ5).
/// </summary>
public enum CvExtractionStatus
{
    /// <summary>Usable text was extracted.</summary>
    Extracted,

    /// <summary>The document had pages/parts but no text layer — almost certainly a
    /// scanned image. Maps to <c>ParseFallbackReason.ScannedImageNoText</c>.</summary>
    NoTextLayer,

    /// <summary>Extraction yielded no text at all (empty or unreadable file). Maps to
    /// <c>ParseFallbackReason.ExtractionFailed</c>.</summary>
    Empty,
}

/// <summary>
/// The result of extracting raw text from a CV file (F4-8). Application-layer
/// <c>record class</c> (CLAUDE.md §3.3) — no PdfPig/OpenXml/EF type crosses the port
/// (parity <c>OccupationDerivationResult</c>). <see cref="RawText"/> is the normalized
/// plain text that feeds both the personnummer scan and persistence; it is CV-PII and
/// must never be logged (ADR 0074 Invariant 3).
/// </summary>
public sealed record CvExtractionResult(
    string RawText,
    CvExtractionStatus Status);
