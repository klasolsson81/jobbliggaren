using Jobbliggaren.Domain.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes.Parsing;

// Fas 4b PR-6b (ADR 0093 §D4, NO AI/LLM) — CvLayoutMetrics is the non-PII structural VO the
// ICvLayoutAnalyzer produces at import and the geometry criteria (B2 page count, D9 file size,
// E2 whitespace) read. Three factories model the three honest outcomes; the LOAD-BEARING
// invariant is that FileSizeBytes is ALWAYS known (so D9 assesses PDF and DOCX alike) while
// PageCount / MinMarginPoints are populated ONLY on the Analyzed (PDF-geometry) arm — the
// determinism never fabricates geometry it could not read. Pure VO ⇒ no clock, no I/O.
public class CvLayoutMetricsTests
{
    // ===============================================================
    // NotApplicable — a DOCX (or any non-PDF): size known, geometry deliberately absent (D10)
    // ===============================================================

    [Fact]
    public void NotApplicable_ShouldSetStatusAndKnownSizeWithNullGeometry_WhenCalled()
    {
        var metrics = CvLayoutMetrics.NotApplicable(fileSizeBytes: 48_123);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.NotApplicable);
        metrics.FileSizeBytes.ShouldBe(48_123);
        metrics.PageCount.ShouldBeNull();
        metrics.MinMarginPoints.ShouldBeNull();
    }

    // ===============================================================
    // Failed — a PDF whose geometry could not be read: size known, geometry null
    // ===============================================================

    [Fact]
    public void Failed_ShouldSetStatusAndKnownSizeWithNullGeometry_WhenCalled()
    {
        var metrics = CvLayoutMetrics.Failed(fileSizeBytes: 8);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Failed);
        metrics.FileSizeBytes.ShouldBe(8);
        metrics.PageCount.ShouldBeNull();
        metrics.MinMarginPoints.ShouldBeNull();
    }

    // ===============================================================
    // Analyzed — PDF geometry read: page count + tightest margin populated
    // ===============================================================

    [Fact]
    public void Analyzed_ShouldSetStatusSizeAndPopulatedGeometry_WhenGeometryWasRead()
    {
        var metrics = CvLayoutMetrics.Analyzed(fileSizeBytes: 262_144, pageCount: 2, minMarginPoints: 56.7);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Analyzed);
        metrics.FileSizeBytes.ShouldBe(262_144);
        metrics.PageCount.ShouldBe(2);
        metrics.MinMarginPoints.ShouldBe(56.7);
    }

    [Fact]
    public void Analyzed_ShouldKeepPageCountButNullMargin_WhenNoPageCarriedLocatableText()
    {
        // A PDF the analyzer opened (so page count is real) but whose pages carried no locatable
        // letters (a scanned/blank body) — the tightest margin is honestly null, never fabricated.
        var metrics = CvLayoutMetrics.Analyzed(fileSizeBytes: 100_000, pageCount: 1, minMarginPoints: null);

        metrics.GeometryStatus.ShouldBe(LayoutGeometryStatus.Analyzed);
        metrics.PageCount.ShouldBe(1);
        metrics.MinMarginPoints.ShouldBeNull();
    }

    // ===============================================================
    // Value equality (record) — the wiring/persistence assertions rely on it
    // ===============================================================

    [Fact]
    public void Analyzed_ShouldBeEqualByValue_WhenAllComponentsMatch()
    {
        var a = CvLayoutMetrics.Analyzed(1234, 2, 40.0);
        var b = CvLayoutMetrics.Analyzed(1234, 2, 40.0);

        a.ShouldBe(b);
        a.ShouldNotBe(CvLayoutMetrics.Analyzed(1234, 3, 40.0));
        a.ShouldNotBe(CvLayoutMetrics.Failed(1234));
    }
}
