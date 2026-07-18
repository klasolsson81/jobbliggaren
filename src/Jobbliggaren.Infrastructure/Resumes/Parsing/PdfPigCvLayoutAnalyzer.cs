using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// <see cref="ICvLayoutAnalyzer"/> via PdfPig (Fas 4b PR-6b, ADR 0093 §D4, NO AI/LLM). PdfPig
/// lives ONLY here — no SDK type crosses the Application port (parity
/// <see cref="PdfPigOpenXmlCvTextExtractor"/>). Reads ONLY non-PII structural geometry (page
/// count + the tightest page margin); it never reconstructs CV text. Fail-soft: a
/// corrupt/encrypted/scanned/non-PDF file never throws — it returns
/// <see cref="LayoutGeometryStatus.Failed"/>/<see cref="LayoutGeometryStatus.NotApplicable"/>
/// with the file size still known (so D9 assesses, B2/E2 verdict NotAssessed on absent
/// geometry). Bounded (a page cap) to stay within the ADR 0045 memory budget and to bound a
/// pathological input; cancellation is honoured between pages (#272 SEC-2 parity).
/// </summary>
internal sealed class PdfPigCvLayoutAnalyzer : ICvLayoutAnalyzer
{
    // Defence-in-depth bound (the import validator already caps input at 10 MiB); parity with
    // the extractor's page cap. Page count is read from the page tree (NumberOfPages) — the cap
    // only bounds the per-page MARGIN scan, so a 400-page bomb reports its true page count for
    // B2 while only 50 pages are geometry-scanned.
    private const int MaxMarginScanPages = 50;

    public CvLayoutMetrics Analyze(
        ReadOnlyMemory<byte> file, CvFileKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fileSizeBytes = file.Length;

        // DOCX layout stays NotAssessed with an honest reason (CTO-bind D10 — no server-side
        // DOCX→PDF conversion); the file size is still a real D9 signal.
        if (kind != CvFileKind.Pdf)
        {
            return CvLayoutMetrics.NotApplicable(fileSizeBytes);
        }

        if (file.IsEmpty)
        {
            return CvLayoutMetrics.Failed(fileSizeBytes);
        }

        try
        {
            using var document = PdfDocument.Open(file.ToArray());

            // B2 reads the TRUE page count from the page tree (cheap — no per-page text/geometry).
            var pageCount = document.NumberOfPages;

            double? minMargin = null;
            // D3 (#891, ADR 0108): tally letters by (raw font name, integer-rounded pt) across the
            // SAME scanned letters the margin uses. A dumb collector — the "body font = modal run"
            // definition and the allowlist match are assessment POLICY and live in the D3 rule.
            var fontTally = new Dictionary<(string FontName, int PointSize), long>();
            var scanned = 0;
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scanned > MaxMarginScanPages)
                {
                    break;
                }

                if (ScanPage(page, fontTally) is { } pageMargin)
                {
                    minMargin = minMargin is { } current ? Math.Min(current, pageMargin) : pageMargin;
                }
            }

            var fontRuns = fontTally.Count == 0
                ? null
                : fontTally
                    // Deterministic order: heaviest run first, then by name then size (ordinal) so
                    // the persisted jsonb is stable for a given document (comparison-by-serialization).
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key.FontName, StringComparer.Ordinal)
                    .ThenBy(kv => kv.Key.PointSize)
                    .Select(kv => new CvFontRun(kv.Key.FontName, kv.Key.PointSize, kv.Value))
                    .ToList();

            return CvLayoutMetrics.Analyzed(fileSizeBytes, pageCount, minMargin, fontRuns);
        }
        catch (OperationCanceledException)
        {
            // A cancellation must propagate, never be swallowed into the fail-soft Failed path.
            throw;
        }
        catch (Exception)
        {
            // Encrypted/corrupt/scanned/malformed PDF — never surface the library exception or
            // any file content; the honest metric is Failed (B2/E2 → NotAssessed downstream).
            return CvLayoutMetrics.Failed(fileSizeBytes);
        }
    }

    // Scans one page's letters ONCE: tallies the (font name, rounded pt) runs into
    // <paramref name="fontTally"/> (D3) AND returns the tightest of the four page-edge margins
    // (PDF points) between the union bounding box of the page's letters and the page's MediaBox
    // (E2). Margin is null when the page carries no letters (a blank/scanned page contributes no
    // signal) or when the text spills outside the media box (rotated/pathological — never fabricate
    // a negative margin into a signal). A letter with a null/blank font name tallies as the empty
    // family, which the D3 rule treats as unresolvable → Warn (never a fabricated allowlist match).
    private static double? ScanPage(Page page, Dictionary<(string FontName, int PointSize), long> fontTally)
    {
        var letters = page.Letters;
        if (letters is null || letters.Count == 0)
        {
            return null;
        }

        double textLeft = double.MaxValue, textRight = double.MinValue;
        double textBottom = double.MaxValue, textTop = double.MinValue;
        foreach (var letter in letters)
        {
            var rect = letter.BoundingBox;
            if (rect.Left < textLeft) textLeft = rect.Left;
            if (rect.Right > textRight) textRight = rect.Right;
            if (rect.Bottom < textBottom) textBottom = rect.Bottom;
            if (rect.Top > textTop) textTop = rect.Top;

            var fontName = letter.FontName ?? string.Empty;
            var pointSize = (int)Math.Round(letter.PointSize, MidpointRounding.AwayFromZero);
            var key = (fontName, pointSize);
            fontTally[key] = fontTally.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var media = page.MediaBox.Bounds;
        var leftMargin = textLeft - media.Left;
        var rightMargin = media.Right - textRight;
        var bottomMargin = textBottom - media.Bottom;
        var topMargin = media.Top - textTop;

        var smallest = Math.Min(Math.Min(leftMargin, rightMargin), Math.Min(bottomMargin, topMargin));
        return smallest >= 0 ? smallest : null;
    }
}
