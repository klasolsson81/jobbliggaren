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
/// text; segmentation and persistence read it. <see cref="AuxiliaryText"/> is the text of
/// a DOCX's headers, footers, footnotes, endnotes and comments, read the same way.
/// <see cref="RevisionText"/> holds, for each group of a DOCX story that carries a tracked
/// change (the stretch between two line ends every reading shares), each of three further
/// readings that differs from how <see cref="RawText"/> or <see cref="AuxiliaryText"/> reads
/// the group: the run text as written, deleted text left out and every paragraph end and break
/// a line end; the text with every change accepted;
/// and the text with every insertion rejected and every deletion kept.
/// <see cref="AuxiliaryText"/> and <see cref="RevisionText"/> are empty for a PDF, and
/// <see cref="RevisionText"/> for a DOCX without tracked changes. The personnummer scan reads
/// all three through <see cref="ScanText"/>, so the scanned surface is larger than
/// <see cref="RawText"/> (#1810, #1803). All three are CV-PII and must never be logged
/// (ADR 0074 Invariant 3).
/// </summary>
public sealed record CvExtractionResult(
    string RawText,
    CvExtractionStatus Status,
    string AuxiliaryText,
    string RevisionText)
{
    /// <summary>What the personnummer scan reads: <see cref="RawText"/>, then
    /// <see cref="AuxiliaryText"/>, then <see cref="RevisionText"/>, each that is not empty on a
    /// line of its own, so no candidate spans a seam.</summary>
    public string ScanText =>
        RawText
        + (AuxiliaryText.Length == 0 ? string.Empty : "\n" + AuxiliaryText)
        + (RevisionText.Length == 0 ? string.Empty : "\n" + RevisionText);
}
