namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// The extraction-level status of a CV text extraction (F4-8). Distinguishes "we got
/// text" from the two manual-fallback triggers a deterministic extractor can detect
/// without segmentation (OQ5). It describes <see cref="CvExtractionResult.RawText"/> only.
/// </summary>
public enum CvExtractionStatus
{
    /// <summary>Usable text was extracted.</summary>
    Extracted,

    /// <summary>The document had pages/parts but no text layer — almost certainly a
    /// scanned image. Maps to <c>ParseFallbackReason.ScannedImageNoText</c>.</summary>
    NoTextLayer,

    /// <summary>No main text was extracted: it is empty, or the file could not be read. Maps to
    /// <c>ParseFallbackReason.ExtractionFailed</c>.</summary>
    Empty,
}

/// <summary>
/// The result of extracting raw text from a CV file (F4-8). Application-layer
/// <c>record class</c> (CLAUDE.md §3.3) — no PdfPig/OpenXml/EF type crosses the port
/// (parity <c>OccupationDerivationResult</c>). <see cref="RawText"/> is the normalized main
/// text, which segmentation and persistence read. <see cref="AuxiliaryText"/> is the text of a
/// DOCX's headers, footers, footnotes, endnotes and comments, and empty for a PDF. The
/// personnummer scan reads both through <see cref="ScanText"/>, so the scanned surface is
/// larger than <see cref="RawText"/> (#1810). Both are CV-PII and must never be logged
/// (ADR 0074 Invariant 3).
/// </summary>
public sealed record CvExtractionResult(
    string RawText,
    CvExtractionStatus Status,
    string AuxiliaryText)
{
    /// <summary>What the personnummer scan reads: <see cref="RawText"/>, then
    /// <see cref="AuxiliaryText"/> on a line of its own, so no candidate spans the seam.</summary>
    public string ScanText => AuxiliaryText.Length == 0 ? RawText : RawText + "\n" + AuxiliaryText;
}
