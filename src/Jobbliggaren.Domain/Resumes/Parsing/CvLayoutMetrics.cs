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
/// One run of letters sharing a font name and (integer-rounded) point size, with the count of
/// letters observed in it (Fas 4b #891, ADR 0108). Collected at IMPORT time by the
/// <c>ICvLayoutAnalyzer</c> alongside the margin scan; carries font NAME + size + count only —
/// never CV text (§5 PII-safe; a font name is not personal data). The D3 "Standardtypsnitt" rule
/// derives the body font/size from these runs at review time (the "body = modal by letter count"
/// definition is assessment POLICY and lives in the rule, not baked into this import artifact).
/// </summary>
/// <param name="FontName">The raw PdfPig font name (subset-tagged/style-suffixed, e.g.
/// "ABCDEF+Arial-BoldMT"); normalised for allowlist matching at review time, never at import.</param>
/// <param name="PointSize">The rendered point size, rounded to the nearest integer (ample for a
/// 9/10/12 pt decision; a bounded FORM reduction, not policy).</param>
/// <param name="LetterCount">How many letters were observed in this (name, size) run.</param>
public sealed record CvFontRun(string FontName, int PointSize, long LetterCount);

/// <summary>
/// Non-PII structural layout metrics for a CV file (Fas 4b PR-6b, ADR 0093 §D4). A Domain
/// value object — parity <see cref="ParseConfidence"/>: produced at IMPORT time by the
/// <c>ICvLayoutAnalyzer</c> port from the source bytes (the only place they exist — the review
/// pipeline sees the decrypted aggregate, never the file), persisted as a plain non-PII column
/// on the staging <c>ParsedResume</c> (NOT the DEK shadow — carries NO CV text), and surfaced
/// to the deterministic review engine so the geometry criteria (B2 page count, D9 file size,
/// E2 whitespace, D3 standard font) can assess an imported PDF.
/// <para>
/// <see cref="FileSizeBytes"/> is ALWAYS known (the import byte length), so D9 assesses PDF
/// and DOCX alike. <see cref="PageCount"/>, <see cref="MinMarginPoints"/> and
/// <see cref="FontRuns"/> are PDF-geometry only (null unless
/// <see cref="LayoutGeometryStatus.Analyzed"/>) — B2/E2/D3 verdict NotAssessed on their absence
/// (DOCX per D10, a failed parse, or the canonical arm which has no metrics at all until PR-9's
/// Form C). The determinism never fabricates geometry it could not read.
/// </para>
/// <para>
/// <see cref="FontRuns"/> rides the SAME nullable jsonb column as the other geometry (no schema
/// migration): a pre-#891 row omits the property and deserialises it to null → D3 NotAssessed,
/// exactly like <see cref="PageCount"/>/<see cref="MinMarginPoints"/> on a pre-PR-6b row.
/// </para>
/// </summary>
public sealed record CvLayoutMetrics(
    LayoutGeometryStatus GeometryStatus,
    long FileSizeBytes,
    int? PageCount,
    double? MinMarginPoints,
    IReadOnlyList<CvFontRun>? FontRuns = null)
{
    /// <summary>DOCX (or any non-PDF) — geometry not analysed, file size known (D10).</summary>
    public static CvLayoutMetrics NotApplicable(long fileSizeBytes) =>
        new(LayoutGeometryStatus.NotApplicable, fileSizeBytes, PageCount: null, MinMarginPoints: null);

    /// <summary>A PDF whose geometry could not be read — file size known, geometry null.</summary>
    public static CvLayoutMetrics Failed(long fileSizeBytes) =>
        new(LayoutGeometryStatus.Failed, fileSizeBytes, PageCount: null, MinMarginPoints: null);

    /// <summary>PDF geometry read: page count + the tightest page-edge margin (PDF points)
    /// across the measured pages (null margin when no page carried locatable text) + the font
    /// runs observed across the scanned pages (null when no page carried locatable text).</summary>
    public static CvLayoutMetrics Analyzed(
        long fileSizeBytes, int pageCount, double? minMarginPoints,
        IReadOnlyList<CvFontRun>? fontRuns = null) =>
        new(LayoutGeometryStatus.Analyzed, fileSizeBytes, pageCount, minMarginPoints, fontRuns);
}
