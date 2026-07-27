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
    /// <para>Deliberately NOT a restatement of any boundary rule. There is no boundary rule in the
    /// tree — one was built for #1060 PR E and withdrawn — but when the next attempt lands, this
    /// proof must stay a DIFFERENT statement about the same bytes: it reads the tightest gap and a
    /// flat point excess, where the withdrawn cut read a median plus half a median line height.
    /// (It would not be fully independent even so: on a single-column page where the tightest gap
    /// IS the median, this proof holding would imply such a cut fires. That makes it a good
    /// detector of a fixture that has ROTTED — which is its job — and a poor confirmation that any
    /// rule works. The corpus verdict columns are what confirm a rule.)</para>
    ///
    /// <para><b>Columns are separated, and that is load-bearing.</b> On a multi-column page the two
    /// columns carry independent baseline series. Merged into one sequence, the tightest "gap"
    /// becomes the smallest offset BETWEEN columns rather than a line pitch — it can be a fraction
    /// of a point — and the threshold collapses so far that ordinary leading counts as authored
    /// spacing. The proof would then hold on a document with no block spacing at all, and
    /// <c>pdf-sidebar-spaced</c>'s claim ("any failure to recover entries here is NOT the document
    /// withholding the boundary") would be silently false. Pass <paramref name="splitX"/> for any
    /// page with a vertical gutter; the gap statistics are then computed WITHIN each column and the
    /// qualifying gaps summed. Same partitioning idea as
    /// <see cref="RequireSharedBaselines"/>.</para>
    /// </summary>
    /// <param name="minCount">Minimum number of qualifying gaps, summed across columns.</param>
    /// <param name="minExtraPoints">How far a gap must exceed its own column's tightest gap.</param>
    /// <param name="splitX">X coordinate separating two columns, or null for a single-column page.</param>
    public void RequireAuthoredParagraphSpacing(int minCount, double minExtraPoints, double? splitX = null)
    {
        var columns = BaselineColumns(splitX);
        if (columns.Count == 0)
        {
            Require(false, "expected authored paragraph spacing; the page carries fewer than two baselines");
            return;
        }

        var spaced = 0;
        var tightestPerColumn = new List<string>();
        foreach (var gaps in columns)
        {
            var tightest = gaps.Min();
            spaced += gaps.Count(g => g >= tightest + minExtraPoints);
            tightestPerColumn.Add(Invariant($"{tightest:F1}"));
        }

        var tightestList = string.Join(" / ", tightestPerColumn);
        Require(spaced >= minCount, Invariant(
            $"expected at least {minCount} gaps exceeding their own column's tightest ({tightestList} pt) by {minExtraPoints} pt or more, found {spaced}"));
    }

    /// <summary>
    /// Proves an authored gap sits between two NAMED lines: the line carrying
    /// <paramref name="upperWord"/> and the line carrying <paramref name="lowerWord"/> are
    /// vertically separated by at least <paramref name="minExtraPoints"/> more than the page's
    /// tightest gap.
    ///
    /// <para>Needed because <see cref="RequireAuthoredParagraphSpacing"/> counts gaps without
    /// knowing WHERE they fall, so it cannot tell a gap BETWEEN two employments from a gap INSIDE
    /// one. <c>pdf-single-column-intra-block-spaced</c> exists precisely to carry the second, and a
    /// case whose distinguishing property no proof establishes is the defect this corpus keeps
    /// finding in itself — a claim that reads as measured and is only authored.</para>
    /// </summary>
    /// <param name="splitX">X coordinate separating two columns, or null for a single-column page.
    /// Required for the same reason <see cref="RequireAuthoredParagraphSpacing"/> needs it: the
    /// reference "tightest gap" is meaningless when two columns' baselines are merged into one
    /// series.</param>
    public void RequireGapBetweenLines(
        string upperWord, string lowerWord, double minExtraPoints, double? splitX = null)
    {
        var anchors = PdfBlockAnchors();

        // Both anchors must be UNIQUE. Without this, editing an unrelated fixture line so that it
        // also contains the word would silently re-point the proof at a different pair of lines and
        // it would keep passing — the "proves something, but not the claim" failure this corpus
        // exists to catch.
        var uppers = anchors.Where(a => a.Text.Contains(upperWord, StringComparison.Ordinal)).ToList();
        var lowers = anchors.Where(a => a.Text.Contains(lowerWord, StringComparison.Ordinal)).ToList();

        Require(uppers.Count == 1, Invariant(
            $"expected '{upperWord}' to occur exactly once on page 1, found {uppers.Count}"));
        Require(lowers.Count == 1, Invariant(
            $"expected '{lowerWord}' to occur exactly once on page 1, found {lowers.Count}"));

        var upper = uppers[0];
        var lower = lowers[0];

        Require(upper.Y > lower.Y, Invariant(
            $"expected '{upperWord}' above '{lowerWord}'; found y = {upper.Y:F1} and {lower.Y:F1}"));

        // The two must be ADJACENT lines. A multi-line span trivially exceeds any gap threshold, so
        // without this the proof would pass on a document where the claimed boundary does not exist
        // and two unrelated lines happen to sit far apart.
        var baselines = BaselineSeries(anchors, splitX, upper.X);
        var upperIndex = baselines.IndexOf(Math.Round(upper.Y, 1));
        var lowerIndex = baselines.IndexOf(Math.Round(lower.Y, 1));

        Require(upperIndex >= 0 && lowerIndex == upperIndex + 1, Invariant(
            $"expected '{upperWord}' and '{lowerWord}' on ADJACENT lines; they are {lowerIndex - upperIndex} lines apart"));

        var columns = BaselineColumns(splitX);
        var tightest = columns.Count == 0 ? 0 : columns.Min(c => c.Min());
        var gap = upper.Y - lower.Y;

        Require(gap >= tightest + minExtraPoints, Invariant(
            $"expected a gap of at least {minExtraPoints} pt over the tightest ({tightest:F1} pt) between '{upperWord}' and '{lowerWord}', found {gap:F1} pt"));
    }

    /// <summary>The rounded baselines of the column containing <paramref name="atX"/>, top to
    /// bottom.</summary>
    private static List<double> BaselineSeries(
        IReadOnlyList<BlockAnchor> anchors, double? splitX, double atX)
    {
        var partition = splitX is null
            ? anchors
            : [.. anchors.Where(a => (a.X < splitX.Value) == (atX < splitX.Value))];

        return [.. partition.Select(a => Math.Round(a.Y, 1)).Distinct().OrderByDescending(y => y)];
    }

    /// <summary>Proves the page carries NO authored paragraph spacing — the exact negation of
    /// <see cref="RequireAuthoredParagraphSpacing"/>, so a case cannot claim both. Declared on the
    /// unspaced twins so the corpus's negative control is STATED rather than assumed: without it,
    /// "this case authors no spacing" is a property of the renderer that no artifact records.
    /// </summary>
    public void RequireUniformLineSpacing(double tolerancePoints, double? splitX = null)
    {
        foreach (var gaps in BaselineColumns(splitX))
        {
            var spread = gaps.Max() - gaps.Min();
            Require(spread <= tolerancePoints, Invariant(
                $"expected uniform leading (spread at most {tolerancePoints} pt), found a spread of {spread:F1} pt"));
        }
    }

    /// <summary>Inter-baseline gaps on page 1, top to bottom, as one list per column. Baselines are
    /// rounded to a tenth of a point so glyph-level jitter does not split one visual line in two.
    /// A column contributing fewer than two baselines yields no gaps and is omitted.</summary>
    private List<List<double>> BaselineColumns(double? splitX)
    {
        var anchors = PdfBlockAnchors();
        var partitions = splitX is null
            ? new[] { anchors }
            : [anchors.Where(a => a.X < splitX.Value).ToList(), anchors.Where(a => a.X >= splitX.Value).ToList()];

        var columns = new List<List<double>>();
        foreach (var partition in partitions)
        {
            var baselines = partition
                .Select(a => Math.Round(a.Y, 1))
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();

            if (baselines.Count < 2)
                continue;

            var gaps = new List<double>();
            for (var i = 1; i < baselines.Count; i++)
                gaps.Add(baselines[i - 1] - baselines[i]);

            columns.Add(gaps);
        }

        return columns;
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
