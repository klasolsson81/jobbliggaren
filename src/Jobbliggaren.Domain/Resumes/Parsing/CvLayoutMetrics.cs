namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Whether PDF page geometry could be analysed for a CV file (Fas 4b PR-6b, ADR 0093 §D4).
/// </summary>
public enum LayoutGeometryStatus
{
    /// <summary>PDF geometry was read — <see cref="CvLayoutMetrics.PageCount"/> +
    /// <see cref="CvLayoutMetrics.MinMarginPoints"/> are populated.</summary>
    Analyzed,

    /// <summary>Not a PDF (DOCX) — layout geometry is deliberately not analysed (CTO-bind D10:
    /// DOCX layout stays NotAssessed, no server-side conversion). File size is still known.</summary>
    NotApplicable,

    /// <summary>A PDF that could not be parsed for geometry (corrupt/encrypted/scanned) —
    /// geometry is null; the honest verdict is NotAssessed. File size is still known.</summary>
    Failed,
}

/// <summary>
/// Non-PII structural layout metrics for a CV file (Fas 4b PR-6b, ADR 0093 §D4). A Domain
/// value object — parity <see cref="ParseConfidence"/>: produced at IMPORT time by the
/// <c>ICvLayoutAnalyzer</c> port from the source bytes (the only place they exist — the review
/// pipeline sees the decrypted aggregate, never the file), persisted as a plain non-PII column
/// on the staging <c>ParsedResume</c> (NOT the DEK shadow — carries NO CV text), and surfaced
/// to the deterministic review engine so the geometry criteria (B2 page count, D9 file size,
/// E2 whitespace) can assess an imported PDF.
/// <para>
/// <see cref="FileSizeBytes"/> is ALWAYS known (the import byte length), so D9 assesses PDF
/// and DOCX alike. <see cref="PageCount"/> and <see cref="MinMarginPoints"/> are PDF-geometry
/// only (null unless <see cref="LayoutGeometryStatus.Analyzed"/>) — B2/E2 verdict NotAssessed
/// on their absence (DOCX per D10, a failed parse, or the canonical arm which has no metrics
/// at all until PR-9's Form C). The determinism never fabricates geometry it could not read.
/// </para>
/// </summary>
public sealed record CvLayoutMetrics(
    LayoutGeometryStatus GeometryStatus,
    long FileSizeBytes,
    int? PageCount,
    double? MinMarginPoints)
{
    /// <summary>DOCX (or any non-PDF) — geometry not analysed, file size known (D10).</summary>
    public static CvLayoutMetrics NotApplicable(long fileSizeBytes) =>
        new(LayoutGeometryStatus.NotApplicable, fileSizeBytes, PageCount: null, MinMarginPoints: null);

    /// <summary>A PDF whose geometry could not be read — file size known, geometry null.</summary>
    public static CvLayoutMetrics Failed(long fileSizeBytes) =>
        new(LayoutGeometryStatus.Failed, fileSizeBytes, PageCount: null, MinMarginPoints: null);

    /// <summary>PDF geometry read: page count + the tightest page-edge margin (PDF points)
    /// across the measured pages (null margin when no page carried locatable text).</summary>
    public static CvLayoutMetrics Analyzed(long fileSizeBytes, int pageCount, double? minMarginPoints) =>
        new(LayoutGeometryStatus.Analyzed, fileSizeBytes, pageCount, minMarginPoints);
}
