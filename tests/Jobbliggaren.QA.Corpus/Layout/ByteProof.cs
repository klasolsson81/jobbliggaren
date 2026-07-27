using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Jobbliggaren.QA.Corpus.Layout;

/// <summary>Thrown when a case's authored bytes do not have the form the case claims. This is an
/// INSTRUMENT failure, never a product finding, and its message says so.</summary>
public sealed class ByteProofException(string caseId, string what)
    : Exception($"INSTRUMENT: fixture '{caseId}' is broken, this is NOT a product regression — {what}")
{
    public string CaseId { get; } = caseId;
}

/// <summary>One word's position on a page, in PDF points, origin bottom-left.</summary>
public sealed record BlockAnchor(double X, double Right, double Y, string Text);

/// <summary>
/// The Tier-1 proof context: it reads the bytes the corpus AUTHORED, through a reader that is not
/// the product's, and answers whether they have the form the case claims.
///
/// <para><b>Why a separate reader is the whole point.</b> The assert rule this corpus lives under
/// is that a hard assert may only take as its subject the bytes we authored, our own declarations,
/// or our own emitter — never what the production chain produced. Proving a case's form through
/// <c>PdfPigOpenXmlCvTextExtractor</c> would violate it: PR E changes that extractor, and every
/// PDF case would go red and block the very fix it exists to measure.</para>
///
/// <para>Geometry is also the only HONEST proof available for the column cases: measured
/// 2026-07-26, <c>ContentOrderTextExtractor</c> linearises by content-stream order, not by page
/// geometry, so an ordering assertion over extracted text would restate the generator rather than
/// measure the product. A vertical gutter cannot be produced by a single-column render, and that
/// claim is independent of emission order.</para>
/// </summary>
public sealed partial class ByteProofContext(string caseId, ReadOnlyMemory<byte> bytes)
{
    public string CaseId { get; } = caseId;

    /// <summary>Physical page count, read straight from the PDF.</summary>
    public int PdfPageCount()
    {
        using var document = PdfDocument.Open(bytes.ToArray());
        return document.NumberOfPages;
    }

    /// <summary>Every word's position on page 1, in points.</summary>
    public IReadOnlyList<BlockAnchor> PdfBlockAnchors()
    {
        using var document = PdfDocument.Open(bytes.ToArray());
        var page = document.GetPage(1);
        return
        [
            .. page.GetWords().Select(w => new BlockAnchor(
                w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom, w.Text)),
        ];
    }

    /// <summary>The OPC main part's raw XML. The serialization FORM is the subject:
    /// <c>&lt;w:p /&gt;</c> versus <c>&lt;w:p&gt;&lt;w:pPr /&gt;&lt;/w:p&gt;</c> decides whether a
    /// blank line exists at all, and the two are indistinguishable from the authoring code.</summary>
    public string DocxDocumentXml()
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new ByteProofException(CaseId, "word/document.xml is absent from the package");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Require(bool condition, string what)
    {
        if (!condition)
            throw new ByteProofException(CaseId, what);
    }

    /// <summary>Proves a vertical gutter exists: an x-interval at least <paramref name="minWidth"/>
    /// points wide, strictly inside the text area, that no word crosses. That is the geometric
    /// signature of a multi-column page and a single-column render cannot produce it.</summary>
    public void RequireVerticalGutter(double minWidth) =>
        Require(WidestGutter() >= minWidth, Invariant(
            $"expected a vertical gutter of at least {minWidth} pt; the widest is {WidestGutter():F1} pt"));

    /// <summary>Proves NO such gutter exists — the single-column claim, stated as the exact
    /// negation of the two-column one so the pair cannot both hold.</summary>
    public void RequireNoVerticalGutter(double minWidth) =>
        Require(WidestGutter() < minWidth, Invariant(
            $"expected no vertical gutter wider than {minWidth} pt, found one of {WidestGutter():F1} pt"));

    /// <summary>Proves at least <paramref name="minCount"/> baselines carry words on BOTH sides of
    /// <paramref name="splitX"/> — the interleaved-row signature, which is what makes two columns'
    /// runs fuse onto one output line.</summary>
    public void RequireSharedBaselines(double splitX, int minCount)
    {
        var shared = PdfBlockAnchors()
            .GroupBy(a => Math.Round(a.Y, 1))
            .Count(g => g.Any(a => a.X < splitX) && g.Any(a => a.X >= splitX));

        Require(shared >= minCount, Invariant(
            $"expected at least {minCount} baselines carrying both columns, found {shared}"));
    }

    /// <summary>
    /// Proves two adjacent CELLS fused into a single word: some word on page 1 contains a digit
    /// immediately followed by a letter, which no ordinary text run produces.
    ///
    /// <para>The obvious proof — "two adjacent words whose horizontal gap is at most N points" —
    /// was written first and measured WRONG (2026-07-27). When the gap reaches zero, PdfPig's own
    /// word segmentation has already merged the two runs into ONE word, so there is no adjacent
    /// pair left to measure a gap between and the proof fails on exactly the documents it was
    /// meant to certify. The fused word IS the signature.</para>
    /// </summary>
    public void RequireDigitLetterFusedWord()
    {
        var fused = PdfBlockAnchors().FirstOrDefault(a => FusedWord().IsMatch(a.Text));
        Require(fused is not null,
            "expected a word fusing two cells (a digit immediately followed by a letter); "
            + "the cells do not abut closely enough for the runs to concatenate");
    }

    /// <summary>Proves a given word sits in the top <paramref name="topFraction"/> of the page's
    /// text area. The failure message deliberately avoids a percent FORMAT: this string is
    /// rendered verbatim into the artifact, and the emitter guard that forbids a percent sign
    /// there runs over hand-built data and could not see it. Paired with the RECORDED output order, this is what turns "emission order is not
    /// geometric order" into a measurement rather than a restatement of the renderer.</summary>
    public void RequireWordNearPageTop(string word, double topFraction)
    {
        var anchors = PdfBlockAnchors();
        var hit = anchors.FirstOrDefault(a => a.Text.Contains(word, StringComparison.Ordinal))
            ?? throw new ByteProofException(
                CaseId, Invariant($"the word '{word}' is absent from page 1"));

        var minY = anchors.Min(a => a.Y);
        var maxY = anchors.Max(a => a.Y);
        var threshold = minY + ((maxY - minY) * (1 - topFraction));

        Require(hit.Y >= threshold, Invariant(
            $"expected '{word}' in the top {topFraction:F2} of the text area (y >= {threshold:F0}), found it at y = {hit.Y:F0}"));
    }

    /// <summary>
    /// Proves the page carries AUTHORED paragraph spacing: at least <paramref name="minCount"/>
    /// inter-baseline gaps exceed the page's tightest gap by at least
    /// <paramref name="minExtraPoints"/> points. The tightest gap is ordinary leading, so the
    /// excess is the space the author put between blocks.
    ///
    /// <para>This is the negation of what every pre-#1060 PDF case proves by omission, and it is
    /// why it exists. Measured 2026-07-27: all thirteen of them emit exactly ONE gap value across
    /// every page, so "zero blank lines in the extracted text" was consistent with two different
    /// worlds — the extractor discarding a boundary, or the document never having one — and the
    /// fixture set could not tell them apart. A spaced case that silently lost its spacing (a
    /// renderer refactor, a QuestPDF upgrade changing how <c>Spacing</c> composes) would look
    /// exactly like a working one: it would simply stop improving, and read as the mechanism
    /// failing rather than the fixture rotting. This proof fails loudly instead.</para>
    ///
    /// <para>Deliberately NOT the production rule. The extractor's boundary cut is a median plus
    /// half a median line height; this reads the tightest gap and a flat point excess. Two
    /// independent statements about the same bytes — if this restated the cut, it would pass
    /// whenever the cut passed and prove nothing about the document.</para>
    /// </summary>
    public void RequireAuthoredParagraphSpacing(int minCount, double minExtraPoints)
    {
        var gaps = InterBaselineGaps();
        if (gaps.Count == 0)
        {
            Require(false, "expected authored paragraph spacing; the page carries fewer than two baselines");
            return;
        }

        var tightest = gaps.Min();
        var spaced = gaps.Count(g => g >= tightest + minExtraPoints);

        Require(spaced >= minCount, Invariant(
            $"expected at least {minCount} gaps exceeding the tightest ({tightest:F1} pt) by {minExtraPoints} pt or more, found {spaced}"));
    }

    /// <summary>Proves the page carries NO authored paragraph spacing — the exact negation of
    /// <see cref="RequireAuthoredParagraphSpacing"/>, so a case cannot claim both.</summary>
    public void RequireUniformLineSpacing(double tolerancePoints)
    {
        var gaps = InterBaselineGaps();
        if (gaps.Count == 0)
            return;

        var spread = gaps.Max() - gaps.Min();
        Require(spread <= tolerancePoints, Invariant(
            $"expected uniform leading (spread at most {tolerancePoints} pt), found a spread of {spread:F1} pt"));
    }

    /// <summary>Gaps between consecutive text baselines on page 1, top to bottom. Baselines are
    /// rounded to a tenth of a point so glyph-level jitter does not split one visual line in two.
    /// </summary>
    private List<double> InterBaselineGaps()
    {
        var baselines = PdfBlockAnchors()
            .Select(a => Math.Round(a.Y, 1))
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        var gaps = new List<double>();
        for (var i = 1; i < baselines.Count; i++)
            gaps.Add(baselines[i - 1] - baselines[i]);

        return gaps;
    }

    private double WidestGutter()
    {
        var anchors = PdfBlockAnchors();
        if (anchors.Count == 0)
            return 0;

        // Sweep the occupied x-intervals and take the widest uncovered span between them.
        var intervals = anchors.Select(a => (Start: a.X, End: a.Right)).OrderBy(i => i.Start).ToList();
        double widest = 0;
        var covered = intervals[0].End;
        foreach (var (start, end) in intervals.Skip(1))
        {
            if (start > covered)
                widest = Math.Max(widest, start - covered);
            covered = Math.Max(covered, end);
        }

        return widest;
    }

    private static string Invariant(FormattableString s) =>
        s.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\d\p{L}")]
    private static partial Regex FusedWord();
}
