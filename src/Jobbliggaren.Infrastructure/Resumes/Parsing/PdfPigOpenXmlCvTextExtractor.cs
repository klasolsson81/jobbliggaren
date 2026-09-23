using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using Jobbliggaren.Application.Resumes.Abstractions;
using UglyToad.PdfPig;
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
            return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty, string.Empty, string.Empty);

        return kind switch
        {
            CvFileKind.Pdf => ExtractPdf(file, cancellationToken),
            CvFileKind.Docx => ExtractDocx(file, cancellationToken),
            _ => new CvExtractionResult(string.Empty, CvExtractionStatus.Empty, string.Empty, string.Empty),
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
                        : CvExtractionStatus.Empty, AuxiliaryText: string.Empty, RevisionText: string.Empty);
            }

            return new CvExtractionResult(text, CvExtractionStatus.Extracted, string.Empty, string.Empty);
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
            return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty, string.Empty, string.Empty);
        }
    }

    // #1810: one budget for the bytes of every part this extractor streams, so the number of parts cannot
    // multiply MaxDecompressedMainPartBytes. The main part is read first and may use all of it.
    private const long MaxDecompressedDocumentBytes = MaxDecompressedMainPartBytes;

    // #1810: ten sections with six header and footer variants each, plus footnotes, endnotes and comments, is 63.
    private const int MaxOtherStories = 64;

    // #1810: a bound on the relationship elements read to list the other stories, whatever their type, so a main
    // part related to many parts costs a fixed amount to list.
    private const int MaxRelationshipsScanned = 1024;

    private const string PackageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

    // #1810: the transitional relationship types of Word's stories outside the main one.
    private static readonly HashSet<string> OtherStoryRelationshipTypes = new(StringComparer.Ordinal)
    {
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments",
    };

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
                return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty, string.Empty, string.Empty);

            // #272 SEC-1: resolve the OPC main-part path via the SDK's relationship
            // resolution (the main part is NOT guaranteed to be "word/document.xml" — it is
            // referenced via _rels), but WITHOUT reading its content stream:
            // mainPart.GetStream() routes through System.IO.Packaging, whose part-stream
            // buffering is not contractually lazy (dotnet/runtime #23750) and could
            // materialize a crafted oversized part before any cap applies. We read only the
            // Uri here and stream the bytes ourselves below.
            Uri mainPartUri;
            using (var packageStream = new MemoryStream(bytes, writable: false))
            using (var document = WordprocessingDocument.Open(packageStream, isEditable: false))
            {
                var mainPart = document.MainDocumentPart;
                if (mainPart is null)
                    return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty, string.Empty, string.Empty);

                mainPartUri = mainPart.Uri;
            }

            // #272 SEC-1: stream the main part's bytes directly via ZipArchive in READ mode —
            // entry.Open() returns a true inflate-on-read DeflateStream (NOT pre-buffered;
            // Update mode would buffer, dotnet/runtime #1544). Wrapping it in a hard
            // byte-counting cap makes the decompressed size bounded DURING inflation, closing
            // the "lying" bomb (small declared size, large actual inflation) regardless of the
            // declared-size guard above.
            using var zipStream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            var entry = archive.GetEntry(EntryName(mainPartUri));
            if (entry is null)
                return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty, string.Empty, string.Empty);

            var budget = new ByteBudget(MaxDecompressedDocumentBytes);
            var mainStory = new StringBuilder();
            var mainRevision = new RevisionRooms();
            using (var partStream = entry.Open())
            using (var cappedStream = new ByteCappedReadStream(partStream, budget, MaxDecompressedMainPartBytes))
            using (var reader = XmlReader.Create(cappedStream, HardenedXmlSettings))
            {
                new StoryReader(mainStory, mainRevision).Read(reader, cancellationToken);
            }

            var otherStories = OtherStories(archive, mainPartUri, entry, budget, cancellationToken);
            var otherRevision = new RevisionRooms();
            return Finalize(
                mainStory, ReadOtherStories(otherStories, budget, otherRevision, cancellationToken), mainRevision, otherRevision);
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
            return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty, string.Empty, string.Empty);
        }
    }

    // #1810: Word's stories outside the main one, in the order the main part's relationships list them. The
    // relationship part is read as a story is, after the main story and under the same budget, and never through the
    // SDK, whose first access to a part collection loads every part the main part relates to. A listing that fails
    // adds no story and keeps the main text.
    private static List<ZipArchiveEntry> OtherStories(
        ZipArchive archive, Uri mainPartUri, ZipArchiveEntry mainEntry, ByteBudget budget, CancellationToken cancellationToken)
    {
        var stories = new List<ZipArchiveEntry>();
        try
        {
            var relationships = archive.GetEntry(EntryName(System.IO.Packaging.PackUriHelper.GetRelationshipPartUri(mainPartUri)));
            if (relationships is null)
                return stories;

            using var partStream = relationships.Open();
            using var cappedStream = new ByteCappedReadStream(partStream, budget);
            using var reader = XmlReader.Create(cappedStream, HardenedXmlSettings);
            var scanned = 0;
            while (stories.Count < MaxOtherStories && reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship"
                    || reader.NamespaceURI != PackageRelationshipsNamespace)
                    continue;

                if (++scanned > MaxRelationshipsScanned)
                    break;

                var story = OtherStory(archive, mainPartUri, reader);
                if (story is not null && story != mainEntry && !stories.Contains(story))
                    stories.Add(story);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stories.Clear();
        }

        return stories;
    }

    // The part a story relationship targets: null for any other relationship, and for a target that cannot be
    // resolved or that the package does not hold, which skips only its own relationship.
    private static ZipArchiveEntry? OtherStory(ZipArchive archive, Uri mainPartUri, XmlReader relationship)
    {
        if (!OtherStoryRelationshipTypes.Contains(relationship.GetAttribute("Type") ?? string.Empty)
            || relationship.GetAttribute("TargetMode") == "External"
            || relationship.GetAttribute("Target") is not { Length: > 0 } target)
            return null;

        try
        {
            var partUri = System.IO.Packaging.PackUriHelper.ResolvePartUri(
                mainPartUri, new Uri(target, UriKind.RelativeOrAbsolute));
            return archive.GetEntry(EntryName(partUri));
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException)
        {
            return null;
        }
    }

    // #1810: the other stories are read for the personnummer scan alone, under what is left of the byte budget. A story
    // the reader rejects adds nothing and costs the main text nothing.
    private static StringBuilder ReadOtherStories(
        IReadOnlyList<ZipArchiveEntry> stories, ByteBudget budget, RevisionRooms revision, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var story in stories)
        {
            if (builder.Length >= MaxOutputChars)
                break;

            var start = builder.Length;
            var revisionStart = revision.Mark();
            try
            {
                StartOwnLine(builder);
                using var partStream = story.Open();
                using var cappedStream = new ByteCappedReadStream(partStream, budget);
                using var reader = XmlReader.Create(cappedStream, HardenedXmlSettings);
                new StoryReader(builder, revision).Read(reader, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                builder.Length = start;
                revision.RollBack(revisionStart);
            }
        }

        return builder;
    }

    private static CvExtractionResult Finalize(
        StringBuilder mainStory, StringBuilder otherStories, RevisionRooms mainRevision, RevisionRooms otherRevision)
    {
        var text = Normalize(mainStory.ToString());
        return new CvExtractionResult(
            text,
            text.Length == 0 ? CvExtractionStatus.Empty : CvExtractionStatus.Extracted,
            Normalize(otherStories.ToString()),
            string.Join('\n', mainRevision.Rooms().Concat(otherRevision.Rooms()).Where(room => room.Length > 0)));
    }

    private static void StartOwnLine(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != '\n')
            builder.Append('\n');
    }

    private static string EntryName(Uri partUri) =>
        Uri.UnescapeDataString(partUri.OriginalString).TrimStart('/');

    // One space between neighbours, never a second one and never at the start of a line.
    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1]))
            builder.Append(' ');
    }

    // A VML shape floats when its CSS declares position: absolute. The declaration is parsed, because Word also
    // writes mso-position-horizontal: absolute, which floats nothing.
    private static bool IsAbsolutelyPositioned(string? style)
    {
        var rest = style.AsSpan();
        while (!rest.IsEmpty)
        {
            var end = rest.IndexOf(';');
            var declaration = end < 0 ? rest : rest[..end];
            rest = end < 0 ? [] : rest[(end + 1)..];

            var colon = declaration.IndexOf(':');
            if (colon >= 0
                && declaration[..colon].Trim().Equals("position", StringComparison.OrdinalIgnoreCase)
                && declaration[(colon + 1)..].Trim().Equals("absolute", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

    // #1803: four readings of a story. R is RawText or AuxiliaryText: R never joins neighbours that the runs as
    // written keep apart. H reads every run as written, leaves deleted text out, and ends a line at every paragraph
    // end and break. A accepts every change. O rejects every insertion and keeps every deletion. A tracked change is
    // a container or a mark; none of its attributes is read.
    private enum Emission { Nothing, Text, Separator, LineEnd, OwnLine }

    private enum StoryEvent { Text, DeletedText, ParagraphEnd, Break, Separator, TextBoxStart }

    private enum Frame
    {
        Other, Paragraph, ParagraphProperties, MarkProperties, RunProperties, RowProperties, CellProperties,
        MathControlProperties, Row, Cell, RemovingContainer, AddingContainer, Text, DeletedText, TabStops,
    }

    // What a revision mark says about the paragraph, row or cell that carries it.
    private readonly record struct Marks(bool Removed, bool Added);

    private readonly record struct Emissions(Emission R, Emission H, Emission A, Emission O)
    {
        // Where every reading ends a line, a group ends. The readings of a group are compared as a whole.
        public bool EndsGroup => EndsLine(R) && EndsLine(H) && EndsLine(A) && EndsLine(O);

        private static bool EndsLine(Emission emission) => emission is Emission.LineEnd or Emission.OwnLine;
    }

    // #1810: one node loop reads every story. #1803: it writes R as it goes and buffers H, A and O for the group, and
    // a group a tracked change touches adds each of them that differs from R and from the others to RevisionText.
    private sealed class StoryReader(StringBuilder accepted, RevisionRooms revision)
    {
        private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        private const string VmlNamespace = "urn:schemas-microsoft-com:vml";
        private const string MathNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/math";

        private readonly StringBuilder _written = new();
        private readonly StringBuilder _joined = new();
        private readonly StringBuilder _rejected = new();

        // Every open element, so that a close undoes what its open did and a child knows its parent.
        private readonly List<Frame> _frames = [];
        private readonly List<Marks> _paragraphs = [];
        private readonly List<Marks> _rows = [];
        private readonly List<Marks> _cells = [];

        private int _removingContainers;
        private int _addingContainers;
        private int _movedFromRanges;
        private int _removedRows;
        private int _addedRows;
        private int _removedCells;
        private int _addedCells;
        private int _markedParagraphs;
        private int _tabStops;
        // The depth of the shape a w:pict or w:object holds; its style says whether it floats.
        private int _vmlShapeDepth = -1;

        private int _groupStart;
        private bool _groupChanged;

        private bool _insideText;
        private bool _insideDeletedText;
        private StoryEvent? _textKind;

        private bool _separatorPending;

        public void Read(XmlReader reader, CancellationToken cancellationToken)
        {
            StartGroup();
            while (reader.Read())
            {
                // #272 SEC-2: cancellable per node.
                cancellationToken.ThrowIfCancellationRequested();

                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        Open(reader);
                        break;

                    case XmlNodeType.EndElement:
                        Close();
                        break;

                    case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace
                        when _textKind is { } textKind:
                        Apply(textKind, default, reader.Value);
                        break;
                }

                if (accepted.Length >= MaxOutputChars)
                    break;
            }

            EndGroup();
        }

        private void Open(XmlReader reader)
        {
            var parent = _frames.Count > 0 ? _frames[^1] : Frame.Other;
            var frame = Frame.Other;
            if (reader.NamespaceURI == WordprocessingMainNamespace)
            {
                switch (reader.LocalName)
                {
                    case "p":
                        frame = Frame.Paragraph;
                        break;
                    case "pPr":
                        frame = Frame.ParagraphProperties;
                        break;
                    case "rPr":
                        frame = parent == Frame.ParagraphProperties ? Frame.MarkProperties : Frame.RunProperties;
                        break;
                    case "trPr":
                        frame = Frame.RowProperties;
                        break;
                    case "tcPr":
                        frame = Frame.CellProperties;
                        break;
                    case "tr":
                        frame = Frame.Row;
                        break;
                    case "tc":
                        frame = Frame.Cell;
                        break;
                    case "t":
                        frame = Frame.Text;
                        _insideText = !reader.IsEmptyElement;
                        _textKind = _insideText ? StoryEvent.Text : _insideDeletedText ? StoryEvent.DeletedText : null;
                        break;
                    case "delText":
                        frame = Frame.DeletedText;
                        _insideDeletedText = !reader.IsEmptyElement;
                        _textKind = _insideDeletedText ? StoryEvent.DeletedText : _insideText ? StoryEvent.Text : null;
                        _groupChanged = true;
                        break;
                    // <w:tabs> defines tab stops under the run tab's local name.
                    case "tabs":
                        frame = Frame.TabStops;
                        break;
                    case "del" or "moveFrom" or "ins" or "moveTo":
                        frame = Revision(reader.LocalName is "del" or "moveFrom", parent);
                        break;
                    case "cellDel" or "cellIns" when parent == Frame.CellProperties:
                        Mark(_cells, reader.LocalName == "cellDel", ref _removedCells, ref _addedCells);
                        break;
                    case "moveFromRangeStart":
                        _movedFromRanges++;
                        _groupChanged = true;
                        break;
                    case "moveFromRangeEnd" when _movedFromRanges > 0:
                        _movedFromRanges--;
                        break;
                    case "br" or "cr":
                        Apply(StoryEvent.Break);
                        break;
                    case "tab" or "ptab" or "sym" when _tabStops == 0:
                        Apply(StoryEvent.Separator);
                        break;
                    case "txbxContent":
                        Apply(StoryEvent.TextBoxStart);
                        break;
                    case "pict" or "object":
                        _vmlShapeDepth = reader.Depth + 1;
                        break;
                }
            }
            else if (reader.NamespaceURI == DrawingNamespace && reader.LocalName == "inline")
            {
                Apply(StoryEvent.Separator);
            }
            else if (reader.NamespaceURI == VmlNamespace && reader.Depth == _vmlShapeDepth && reader.LocalName != "shapetype")
            {
                if (!IsAbsolutelyPositioned(reader.GetAttribute("style")))
                    Apply(StoryEvent.Separator);
            }
            else if (reader.NamespaceURI == MathNamespace && reader.LocalName == "ctrlPr")
            {
                frame = Frame.MathControlProperties;
            }

            // A self-closing element has no end element, so it opens nothing that a close would undo.
            if (reader.IsEmptyElement)
                return;

            _frames.Add(frame);
            switch (frame)
            {
                case Frame.Paragraph:
                    _paragraphs.Add(default);
                    break;
                case Frame.Row:
                    _rows.Add(default);
                    break;
                case Frame.Cell:
                    _cells.Add(default);
                    break;
                case Frame.RemovingContainer:
                    _removingContainers++;
                    _groupChanged = true;
                    break;
                case Frame.AddingContainer:
                    _addingContainers++;
                    _groupChanged = true;
                    break;
                case Frame.TabStops:
                    _tabStops++;
                    break;
            }
        }

        // Under properties a revision element marks what the properties belong to; anywhere else it holds content.
        private Frame Revision(bool removes, Frame parent)
        {
            switch (parent)
            {
                case Frame.MarkProperties when _paragraphs.Count > 0:
                    var mark = _paragraphs[^1];
                    if (!mark.Removed && !mark.Added)
                        _markedParagraphs++;
                    _paragraphs[^1] = removes ? mark with { Removed = true } : mark with { Added = true };
                    _groupChanged = true;
                    return Frame.Other;
                case Frame.RowProperties:
                    Mark(_rows, removes, ref _removedRows, ref _addedRows);
                    return Frame.Other;
                case Frame.MarkProperties or Frame.RunProperties or Frame.CellProperties or Frame.MathControlProperties:
                    return Frame.Other;
                default:
                    return removes ? Frame.RemovingContainer : Frame.AddingContainer;
            }
        }

        private void Mark(List<Marks> open, bool removes, ref int removed, ref int added)
        {
            if (open.Count == 0)
                return;

            var marks = open[^1];
            if (removes && !marks.Removed)
            {
                removed++;
                open[^1] = marks with { Removed = true };
            }
            else if (!removes && !marks.Added)
            {
                added++;
                open[^1] = marks with { Added = true };
            }

            _groupChanged = true;
        }

        private void Close()
        {
            var frame = _frames[^1];
            _frames.RemoveAt(_frames.Count - 1);
            switch (frame)
            {
                case Frame.Paragraph:
                    var mark = _paragraphs[^1];
                    _paragraphs.RemoveAt(_paragraphs.Count - 1);
                    if (mark.Removed || mark.Added)
                        _markedParagraphs--;
                    Apply(StoryEvent.ParagraphEnd, mark);
                    break;
                case Frame.Row:
                    Unmark(_rows, ref _removedRows, ref _addedRows);
                    break;
                case Frame.Cell:
                    Unmark(_cells, ref _removedCells, ref _addedCells);
                    break;
                case Frame.RemovingContainer:
                    _removingContainers--;
                    break;
                case Frame.AddingContainer:
                    _addingContainers--;
                    break;
                case Frame.TabStops:
                    _tabStops--;
                    break;
                case Frame.Text:
                    _insideText = false;
                    _textKind = _insideDeletedText ? StoryEvent.DeletedText : null;
                    break;
                case Frame.DeletedText:
                    _insideDeletedText = false;
                    _textKind = _insideText ? StoryEvent.Text : null;
                    break;
            }
        }

        private static void Unmark(List<Marks> open, ref int removed, ref int added)
        {
            var marks = open[^1];
            open.RemoveAt(open.Count - 1);
            if (marks.Removed)
                removed--;
            if (marks.Added)
                added--;
        }

        private void Apply(StoryEvent storyEvent, Marks mark = default, string text = "")
        {
            var emissions = EmissionsFor(storyEvent, mark);
            if (emissions.EndsGroup)
            {
                EndGroup();
                WriteAccepted(storyEvent, emissions.R, text);
                StartGroup();
                return;
            }

            WriteAccepted(storyEvent, emissions.R, text);
            Write(_written, emissions.H, text, revision.WrittenSpace);
            Write(_joined, emissions.A, text, revision.ChangedSpace);
            Write(_rejected, emissions.O, text, revision.ChangedSpace);
        }

        private Emissions EmissionsFor(StoryEvent storyEvent, Marks mark)
        {
            // R accepts only the run-level containers; A and O also read deleted rows, cells and moved ranges.
            var runRemoved = _removingContainers > 0;
            var removed = runRemoved || _movedFromRanges > 0 || _removedRows > 0 || _removedCells > 0;
            // A deletion inside an insertion is kept by the rejected reading.
            var added = !removed && (_addingContainers > 0 || _addedRows > 0 || _addedCells > 0);

            return storyEvent switch
            {
                StoryEvent.Text => new(
                    runRemoved ? Emission.Separator : Emission.Text,
                    Emission.Text,
                    removed ? Emission.Nothing : Emission.Text,
                    added ? Emission.Nothing : Emission.Text),
                StoryEvent.DeletedText => new(Emission.Nothing, Emission.Nothing, Emission.Nothing, Emission.Text),
                StoryEvent.ParagraphEnd => new(
                    mark.Removed || runRemoved ? Emission.Separator : Emission.LineEnd,
                    Emission.LineEnd,
                    mark.Removed ? Emission.Nothing : Emission.LineEnd,
                    mark.Added ? Emission.Nothing : Emission.LineEnd),
                StoryEvent.Break => new(
                    runRemoved ? Emission.Separator : Emission.LineEnd,
                    Emission.LineEnd,
                    removed ? Emission.Nothing : Emission.LineEnd,
                    added ? Emission.Nothing : Emission.LineEnd),
                StoryEvent.Separator => new(
                    Emission.Separator,
                    Emission.Separator,
                    removed ? Emission.Nothing : Emission.Separator,
                    added ? Emission.Nothing : Emission.Separator),
                StoryEvent.TextBoxStart => new(
                    runRemoved ? Emission.Separator : Emission.OwnLine,
                    Emission.OwnLine,
                    Emission.OwnLine,
                    Emission.OwnLine),
                _ => throw new ArgumentOutOfRangeException(nameof(storyEvent), storyEvent, null),
            };
        }

        private void WriteAccepted(StoryEvent storyEvent, Emission emission, string text)
        {
            if (emission == Emission.Separator && storyEvent is StoryEvent.ParagraphEnd or StoryEvent.Break or StoryEvent.TextBoxStart)
            {
                _separatorPending = true;
                return;
            }

            if (emission is Emission.LineEnd or Emission.OwnLine)
            {
                _separatorPending = false;
            }
            else if (_separatorPending && emission is Emission.Text or Emission.Separator)
            {
                _separatorPending = false;
                Write(accepted, Emission.Separator, string.Empty, MaxOutputChars);
            }

            Write(accepted, emission, text, MaxOutputChars);
        }

        // #268 SEC-1: nothing is appended past the limit, so a single oversized node is truncated, never appended whole.
        private static void Write(StringBuilder builder, Emission emission, string text, int limit)
        {
            if (builder.Length >= limit)
                return;

            switch (emission)
            {
                case Emission.Text:
                    builder.Append(text.AsSpan(0, Math.Min(text.Length, limit - builder.Length)));
                    break;
                case Emission.Separator:
                    AppendSeparator(builder);
                    break;
                case Emission.LineEnd:
                    builder.Append('\n');
                    break;
                case Emission.OwnLine:
                    StartOwnLine(builder);
                    break;
            }
        }

        private void StartGroup()
        {
            _groupStart = accepted.Length;
            _written.Clear();
            _joined.Clear();
            _rejected.Clear();
            // A group that starts inside an open change carries it.
            _groupChanged = _removingContainers > 0 || _addingContainers > 0 || _movedFromRanges > 0
                || _removedRows > 0 || _addedRows > 0 || _removedCells > 0 || _addedCells > 0 || _markedParagraphs > 0;
        }

        private void EndGroup()
        {
            if (!_groupChanged)
                return;

            List<string> added = [accepted.ToString(_groupStart, accepted.Length - _groupStart)];
            var written = _written.ToString();
            if (IsNew(written))
            {
                revision.AddWritten(written);
                added.Add(written);
            }

            foreach (var reading in new[] { _joined.ToString(), _rejected.ToString() })
            {
                if (IsNew(reading))
                {
                    revision.AddChanged(reading);
                    added.Add(reading);
                }
            }

            bool IsNew(string reading) => !added.Contains(reading);
        }
    }

    // #1803: RevisionText's rooms for one kind of story, MaxOutputChars each. H has a room of its own, so A and O can
    // never crowd out what H reaches, and the scan keeps everything it read before tracked changes were read.
    private sealed class RevisionRooms
    {
        private readonly StringBuilder _written = new();
        private readonly StringBuilder _changed = new();

        public int WrittenSpace => MaxOutputChars - _written.Length;

        public int ChangedSpace => MaxOutputChars - _changed.Length;

        public void AddWritten(string reading) => Add(_written, reading);

        public void AddChanged(string reading) => Add(_changed, reading);

        public (int Written, int Changed) Mark() => (_written.Length, _changed.Length);

        public void RollBack((int Written, int Changed) mark)
        {
            _written.Length = mark.Written;
            _changed.Length = mark.Changed;
        }

        public IEnumerable<string> Rooms() => [Normalize(_written.ToString()), Normalize(_changed.ToString())];

        // Each reading starts its own line, so no candidate spans two readings.
        private static void Add(StringBuilder room, string reading)
        {
            if (room.Length >= MaxOutputChars)
                return;

            StartOwnLine(room);
            room.Append(reading.AsSpan(0, Math.Min(reading.Length, Math.Max(0, MaxOutputChars - room.Length))));
        }
    }

    // #1810: what the parts this extractor streams from one document may inflate together. A part that fails
    // is still charged.
    private sealed class ByteBudget(long bytes)
    {
        private long _remaining = bytes;

        public void Charge(int n)
        {
            _remaining -= n;
            if (_remaining < 0)
                throw new InvalidDataException("Decompressed DOCX parts exceeded the document's byte budget.");
        }
    }

    // #272 SEC-1: a read-only stream wrapper that hard-caps the decompressed bytes read from
    // the underlying DeflateStream and charges them to the document's budget (#1810). It
    // throws once either is crossed, so the "lying" zip bomb is bounded DURING inflation,
    // not after materialization. Never surfaces file content in the message.
    private sealed class ByteCappedReadStream(Stream inner, ByteBudget budget, long cap = long.MaxValue) : Stream
    {
        private long _read;

        private int Account(int n)
        {
            _read += n;
            budget.Charge(n);
            if (_read > cap)
                throw new InvalidDataException(
                    "Decompressed DOCX part exceeded the allowed byte ceiling.");
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
