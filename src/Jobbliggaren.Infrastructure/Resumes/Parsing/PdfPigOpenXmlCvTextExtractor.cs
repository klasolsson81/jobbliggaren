using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using Jobbliggaren.Application.Resumes.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Deterministic CV text extraction behind <see cref="ICvTextExtractor"/> (F4-8,
/// NO AI/LLM). PdfPig (PDF) and DocumentFormat.OpenXml (DOCX) live ONLY here — no SDK
/// type crosses the Application port. Fail-soft: a corrupt/encrypted/oversized/scanned
/// file never throws — it returns a fallback status so the handler routes to manual
/// entry (OQ5). Bounded work keeps extraction within the ADR 0045 memory budget and
/// mitigates zip/decompression bombs: a page cap + output-char cap (both paths), and on
/// the DOCX path a pre-decompression declared-size guard + per-node truncation (#268
/// SEC-1) PLUS a hard byte-cap on the actually-decompressed main part read through a
/// hardened <see cref="XmlReader"/> (#272 SEC-1 — closes the "lying" zip bomb that
/// declares a small uncompressed size but inflates a large single node).
/// <para>
/// <b>Cancellation (#272 SEC-2):</b> the synchronous extraction honours a
/// <see cref="CancellationToken"/> cooperatively (checked per PDF page / per DOCX read)
/// so a pathological input is interruptible; an <see cref="OperationCanceledException"/>
/// propagates (it is NOT swallowed into the fail-soft <c>Empty</c> path).
/// </para>
/// </summary>
internal sealed class PdfPigOpenXmlCvTextExtractor : ICvTextExtractor
{
    // Defence-in-depth bounds (the validator already caps the input at 10 MiB).
    private const int MaxPages = 50;
    private const int MaxOutputChars = 1_000_000;

    // #1060 PR E — the paragraph-boundary rule. See the block comment above
    // InsertParagraphBoundaries for what these mean and for the measurements behind them.

    // How much clear space above a line makes it a new paragraph, in line heights, ON TOP OF the
    // page's own body pitch. The ONE authored number in the rule; the two values it combines
    // (body pitch, line height) are both read from the page. A ratio of the body pitch was tried
    // instead and measured wrong — corpus baseline appendix C.
    private const double ParagraphGapInLineHeights = 0.5;

    // Below this many text lines a page has too few gaps for a median to mean anything, so no
    // boundary is inferred at all (a two-line page has one gap, which is its own median).
    private const int MinLinesForParagraphBoundary = 4;

    // Baselines are rounded to a tenth of a point before grouping so sub-glyph jitter cannot
    // split one visual line into two.
    private const int BaselineRoundingDigits = 1;

    // #268 SEC-1 (DOCX decompression/zip bomb): DOCX is a ZIP/OPC container and the
    // validator caps only the COMPRESSED input at 10 MiB. DEFLATE permits ~1000:1, so a
    // small .docx can declare/expand to multi-GB. Reject before decompression when the
    // package's total DECLARED uncompressed size exceeds this ceiling (catches the honest
    // bomb; cheap, no inflation) — generous for a real CV (KB–low-MB), far below RAM.
    private const long MaxUncompressedBytes = 64L * 1024 * 1024;

    // #272 SEC-1: a HARD cap on the bytes actually decompressed from the main document
    // part, enforced DURING inflation (the declared-size guard above can be lied about).
    // 16 MiB is an order of magnitude above a real CV's document.xml (text + markup
    // overhead) yet far below RAM exhaustion on the low-RAM VPS (ADR 0045). Hitting it
    // means a crafted bomb → fail-soft Empty.
    private const long MaxDecompressedMainPartBytes = 16L * 1024 * 1024;

    // The WordprocessingML main namespace — w:t (text) and w:p (paragraph) live here.
    private const string WordprocessingMainNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // #272 SEC-1 (XXE / billion-laughs / external-entity): the hand-rolled XmlReader that
    // replaces OpenXmlReader MUST be hardened — a DOCX is attacker-controlled. DTDs are
    // prohibited (a legitimate document.xml never carries one; a DOCTYPE → throw →
    // fail-soft Empty), no resolver (no external entity / external DTD fetch → no
    // SSRF/file-read), and entity/document char ceilings as belt-and-suspenders.
    // (OWASP XXE Prevention Cheat Sheet.)
    private static readonly XmlReaderSettings HardenedXmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = MaxOutputChars * 4L,
        CloseInput = true,
        IgnoreProcessingInstructions = true,
        IgnoreComments = true,
    };

    public CvExtractionResult Extract(
        ReadOnlyMemory<byte> file, CvFileKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (file.IsEmpty)
            return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);

        return kind switch
        {
            CvFileKind.Pdf => ExtractPdf(file, cancellationToken),
            CvFileKind.Docx => ExtractDocx(file, cancellationToken),
            _ => new CvExtractionResult(string.Empty, CvExtractionStatus.Empty),
        };
    }

    private static CvExtractionResult ExtractPdf(ReadOnlyMemory<byte> file, CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(file.ToArray());

            var builder = new StringBuilder();
            var pageCount = 0;

            foreach (var page in document.GetPages())
            {
                // #272 SEC-2: cancellable BETWEEN pages (PdfPig's GetText is not itself
                // cancellable; the page/char caps + 10 MiB input cap bound a single page).
                cancellationToken.ThrowIfCancellationRequested();

                if (++pageCount > MaxPages)
                    break;

                // Content-order extraction reconstructs reading order with spacing
                // (raw page.Text concatenates glyphs without reliable spaces).
                var pageText = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrEmpty(pageText))
                {
                    // #1060 PR E: restore the paragraph boundaries the author put in the page
                    // and this API discards. Adds blank lines ONLY; never alters a character.
                    pageText = InsertParagraphBoundaries(page, pageText);

                    // #272 SEC-2: bound EACH page to the remaining char budget so a single
                    // pathological page is truncated to the cap, not appended whole.
                    var remaining = MaxOutputChars - builder.Length;
                    if (pageText.Length >= remaining)
                    {
                        builder.Append(pageText.AsSpan(0, remaining));
                        break;
                    }

                    builder.Append(pageText).Append('\n');
                    if (builder.Length >= MaxOutputChars)
                        break;
                }
            }

            var text = Normalize(builder.ToString());

            if (text.Length == 0)
            {
                // Pages existed but yielded no text layer ⇒ almost certainly scanned.
                return new CvExtractionResult(
                    string.Empty,
                    document.NumberOfPages > 0
                        ? CvExtractionStatus.NoTextLayer
                        : CvExtractionStatus.Empty);
            }

            return new CvExtractionResult(text, CvExtractionStatus.Extracted);
        }
        catch (OperationCanceledException)
        {
            // #272 SEC-2: a cancellation must propagate, NOT be swallowed into the
            // fail-soft Empty path below (that would silently mask a timeout).
            throw;
        }
        catch (Exception)
        {
            // Encrypted/corrupt/malformed PDF — never surface the library exception or
            // any file content; route to manual fallback (OQ5).
            return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);
        }
    }

    // ---- #1060 PR E: the paragraph boundary -------------------------------------------------
    //
    // WHAT THIS FIXES. `SplitEntries` in the segmenter splits a section block into entries on
    // BLANK LINES and on nothing else. A PDF page carries the boundary between two employments
    // geometrically — as extra vertical space — and `ContentOrderTextExtractor` emits one '\n'
    // per visual line regardless, so the boundary is discarded before the segmenter ever sees it.
    // Five employments arrive as one entry, `SplitTitleOrganization` reads job 1's header line,
    // `ValidateContent` passes, and the CV promotes CONFIDENTLY with four employments missing.
    //
    // WHY NOT `GetText(page, addDoubleNewline: true)`, which looks like the one-line version of
    // this (measured 2026-07-26, recorded in the corpus baseline appendix A): the flag is a LINE
    // signal, not a paragraph one. It inserts a blank line after EVERY visual line, so the same
    // bytes go 0 -> 42 blank lines and 5 -> 15 parsed experiences, one of them literally
    // title=<null> org=<null> period="2021 - 2026". Such a fragment projects as an empty Company
    // and `Resume.ValidateContent` returns on the first one, so every PDF whose periods sit on
    // their own line would stop promoting entirely. An honest block is the better outcome for ONE
    // document; it is not a policy for a container format.
    //
    // WHY THE CUT IS `median gap + 0.5 * median line height` AND NOT A RATIO (measured
    // 2026-07-27, corpus baseline appendix C). A 1.5x multiple of the body pitch was tried first
    // and is WRONG on the most realistic document built: at 1.35 line spacing with 6 pt paragraph
    // gaps the body pitch is 14.9 pt and the paragraph gaps are 20.8 pt, while 1.5x is 22.35 pt —
    // it catches nothing. The half-line form catches every authored boundary on that document
    // (cut 18.95) and on a wider-spaced one (cut 17.24, boundaries 27.4 and 31.4), and yields
    // ZERO boundaries on a document with uniform leading. Both quantities are read from the page;
    // 0.5 is the only authored number here, it means "half a line of extra whitespace", and the
    // measurement above is what a future reader tempted to retune it should read first.
    //
    // WHY IT CANNOT CORRUPT TEXT. The emitted string is always `ContentOrderTextExtractor`'s own
    // output with blank lines INSERTED; geometry contributes positions and never characters. The
    // insertion happens only when this method's independently-derived line list matches that
    // output line for line, so a page whose reading order is not top-to-bottom (a two-column
    // sidebar, a layered decorative page) falls through to today's behaviour instead of being
    // silently misaligned. That guard is per-page.
    //
    // WHAT IT DOES NOT FIX (deliberate, measured): a two-column/sidebar CV. The two columns share
    // baselines, so the line list cannot match the extractor's column-sequential order and the
    // guard trips by construction — the corpus carries this as `pdf-sidebar-spaced`, a document
    // WITH authored spacing that still does not improve. Same-baseline column merging and
    // negative-x-gap concatenation are likewise untouched; all three need column-aware layout
    // analysis, which is named here and not promised.

    /// <summary>
    /// Returns <paramref name="pageText"/> with a blank line inserted at each authored paragraph
    /// boundary, or <paramref name="pageText"/> unchanged when the page carries no usable signal.
    /// Never removes or rewrites a character.
    /// </summary>
    private static string InsertParagraphBoundaries(Page page, string pageText)
    {
        try
        {
            // Split on '\n' only; a trailing '\r' stays attached to its line and is folded by
            // Normalize() later, exactly as it is today.
            var emitted = pageText.Split('\n');

            // The extractor's own lines, with blanks ignored — the authority for both ORDER and
            // TEXT. Only their positions are in question here.
            var authority = new List<int>();
            for (var i = 0; i < emitted.Length; i++)
            {
                if (emitted[i].Trim().Length > 0)
                    authority.Add(i);
            }

            if (authority.Count < MinLinesForParagraphBoundary)
                return pageText;

            var lines = GeometricLines(page);
            if (lines.Count != authority.Count)
                return pageText;

            for (var i = 0; i < lines.Count; i++)
            {
                if (!SameIgnoringWhitespace(emitted[authority[i]], lines[i].Text))
                    return pageText;
            }

            var boundaries = BoundaryIndices(lines);
            if (boundaries.Count == 0)
                return pageText;

            var builder = new StringBuilder(pageText.Length + boundaries.Count + 1);
            var ordinal = 0;
            for (var i = 0; i < emitted.Length; i++)
            {
                if (i > 0)
                    builder.Append('\n');

                if (emitted[i].Trim().Length > 0)
                {
                    if (boundaries.Contains(ordinal))
                        builder.Append('\n');

                    ordinal++;
                }

                builder.Append(emitted[i]);
            }

            return builder.ToString();
        }
        catch (Exception)
        {
            // The page geometry is unreadable for this page. Fall back to the text the extractor
            // already produced — NOT to the enclosing fail-soft Empty path, which would throw away
            // text we successfully extracted and make this a regression on a contract whose whole
            // point is that it never loses more than it has to. (Not a bare catch-all: the action
            // is a defined fallback to the pre-#1060 output.)
            return pageText;
        }
    }

    /// <summary>One entry per visual line: its baseline, its words joined left to right, and the
    /// tallest glyph box on it. Independent of the extractor's own line assembly on purpose — the
    /// caller compares the two and only proceeds when they agree.</summary>
    private static List<GeometricLine> GeometricLines(Page page) =>
        [.. page.GetWords()
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom, BaselineRoundingDigits))
            .OrderByDescending(g => g.Key)
            .Select(g => new GeometricLine(
                g.Key,
                string.Join(' ', g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)),
                g.Max(w => w.BoundingBox.Height)))];

    /// <summary>The line indices that START a new paragraph: those whose gap to the previous line
    /// exceeds the page's own body pitch by at least half a line. Both reference values are
    /// medians of the page, so a densely-set page and an airy one are judged by their own
    /// spacing.</summary>
    private static HashSet<int> BoundaryIndices(List<GeometricLine> lines)
    {
        var gaps = new double[lines.Count - 1];
        for (var i = 1; i < lines.Count; i++)
            gaps[i - 1] = lines[i - 1].Baseline - lines[i].Baseline;

        var bodyPitch = Median(gaps);
        var lineHeight = Median([.. lines.Select(l => l.Height)]);
        var cut = bodyPitch + (ParagraphGapInLineHeights * lineHeight);

        var boundaries = new HashSet<int>();
        for (var i = 1; i < lines.Count; i++)
        {
            if (gaps[i - 1] > cut)
                boundaries.Add(i);
        }

        return boundaries;
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
            return 0;

        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static bool SameIgnoringWhitespace(string left, string right)
    {
        int i = 0, j = 0;
        while (true)
        {
            while (i < left.Length && char.IsWhiteSpace(left[i]))
                i++;
            while (j < right.Length && char.IsWhiteSpace(right[j]))
                j++;

            if (i == left.Length || j == right.Length)
                return i == left.Length && j == right.Length;

            if (left[i] != right[j])
                return false;

            i++;
            j++;
        }
    }

    /// <summary>A visual line's geometry: PDF baseline (y grows upward), its text, and the
    /// tallest glyph box on it.</summary>
    private readonly record struct GeometricLine(double Baseline, string Text, double Height);

    private static CvExtractionResult ExtractDocx(ReadOnlyMemory<byte> file, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = file.ToArray();

            // #268 SEC-1: pre-flight zip-bomb guard — reject before decompression if the OPC
            // package's total DECLARED uncompressed size blows past the ceiling. Catches the
            // honest bomb without inflating a single byte (defence-in-depth; the declared size
            // can be lied about, so it is NOT the authoritative bound — the byte-cap below is).
            if (DeclaredUncompressedBytesExceed(bytes, MaxUncompressedBytes))
                return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);

            // #272 SEC-1: resolve the OPC main-part path via the SDK's relationship
            // resolution (the main part is NOT guaranteed to be "word/document.xml" — it is
            // referenced via _rels), but WITHOUT reading its content stream:
            // mainPart.GetStream() routes through System.IO.Packaging, whose part-stream
            // buffering is not contractually lazy (dotnet/runtime #23750) and could
            // materialize a crafted oversized part before any cap applies. We read only the
            // Uri here and stream the bytes ourselves below.
            string mainPartPath;
            using (var packageStream = new MemoryStream(bytes, writable: false))
            using (var document = WordprocessingDocument.Open(packageStream, isEditable: false))
            {
                var mainPart = document.MainDocumentPart;
                if (mainPart is null)
                    return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);

                mainPartPath = Uri.UnescapeDataString(mainPart.Uri.OriginalString).TrimStart('/');
            }

            // #272 SEC-1: stream the main part's bytes directly via ZipArchive in READ mode —
            // entry.Open() returns a true inflate-on-read DeflateStream (NOT pre-buffered;
            // Update mode would buffer, dotnet/runtime #1544). Wrapping it in a hard
            // byte-counting cap makes the decompressed size bounded DURING inflation, closing
            // the "lying" bomb (small declared size, large actual inflation) regardless of the
            // declared-size guard above.
            using var zipStream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            var entry = archive.GetEntry(mainPartPath);
            if (entry is null)
                return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);

            using var partStream = entry.Open();
            using var cappedStream = new ByteCappedReadStream(partStream, MaxDecompressedMainPartBytes);
            using var reader = XmlReader.Create(cappedStream, HardenedXmlSettings);

            var builder = new StringBuilder();
            var insideText = false;

            while (reader.Read())
            {
                // #272 SEC-2: cancellable per node.
                cancellationToken.ThrowIfCancellationRequested();

                switch (reader.NodeType)
                {
                    case XmlNodeType.Element
                        when reader.LocalName == "t" && reader.NamespaceURI == WordprocessingMainNamespace:
                        // <w:t> carries the run text; an empty element has no content node.
                        insideText = !reader.IsEmptyElement;
                        break;

                    case XmlNodeType.EndElement
                        when reader.LocalName == "t" && reader.NamespaceURI == WordprocessingMainNamespace:
                        insideText = false;
                        break;

                    case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace
                        when insideText:
                        // #268 SEC-1: bound the appended text to the remaining char budget so a
                        // single oversized node is truncated, never appended whole.
                        var node = reader.Value;
                        var remaining = MaxOutputChars - builder.Length;
                        if (node.Length >= remaining)
                        {
                            builder.Append(node.AsSpan(0, remaining));
                            return Finalize(builder);
                        }

                        builder.Append(node);
                        break;

                    case XmlNodeType.EndElement
                        when reader.LocalName == "p" && reader.NamespaceURI == WordprocessingMainNamespace:
                        builder.Append('\n');
                        break;
                }

                if (builder.Length >= MaxOutputChars)
                    break;
            }

            return Finalize(builder);
        }
        catch (OperationCanceledException)
        {
            // #272 SEC-2: cancellation propagates, never swallowed into Empty.
            throw;
        }
        catch (Exception)
        {
            // Not a valid OPC package / corrupt DOCX / XML-bomb (DTD rejected) / byte-cap
            // exceeded — fail soft. Never logs the exception or any file content (CLAUDE.md §5).
            return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);
        }
    }

    private static CvExtractionResult Finalize(StringBuilder builder)
    {
        var text = Normalize(builder.ToString());
        return text.Length == 0
            ? new CvExtractionResult(string.Empty, CvExtractionStatus.Empty)
            : new CvExtractionResult(text, CvExtractionStatus.Extracted);
    }

    // #268 SEC-1: sum the OPC package's DECLARED uncompressed entry sizes (read from the
    // ZIP central directory — no decompression) and report whether the total exceeds the
    // ceiling. Defence-in-depth: a genuine bomb declares a multi-GB size, rejected before
    // WordprocessingDocument inflates anything. Reading the central directory is bounded by
    // the 10 MiB compressed input cap.
    private static bool DeclaredUncompressedBytesExceed(byte[] bytes, long ceiling)
    {
        using var zipStream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            total += entry.Length;
            if (total > ceiling)
                return true;
        }

        return false;
    }

    // Deterministic newline normalization (CRLF/CR → LF) and trimming. Preserves åäö
    // (UTF-8 strings from both libraries are already Unicode); no culture-dependent ops.
    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    // #272 SEC-1: a read-only stream wrapper that hard-caps the total decompressed bytes
    // read from the underlying DeflateStream. Throws once the cap is crossed so the
    // surrounding catch fails soft to Empty (the "lying" zip bomb is bounded DURING
    // inflation, not after materialization). Never surfaces file content in the message.
    private sealed class ByteCappedReadStream(Stream inner, long cap) : Stream
    {
        private long _read;

        private int Account(int n)
        {
            _read += n;
            if (_read > cap)
                throw new InvalidDataException(
                    "Decompressed DOCX main part exceeded the allowed byte ceiling.");
            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Account(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) =>
            Account(inner.Read(buffer));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
