using UglyToad.PdfPig.Content;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// The physical lines of one PDF page, as PdfPig's <c>ContentOrderTextExtractor.GetText</c> (0.1.16)
/// lays them out, with the geometry <c>GetText</c> discards (#2353, senior-cto-advisor D4).
/// <c>GetText</c> stays the text source. This type groups the same letters by <c>GetText</c>'s own
/// line-break test and aligns each output line's non-whitespace characters to its letters. A page
/// whose lines or characters do not align yields null, and the extractor then measures nothing.
/// </summary>
internal static class PdfPageLines
{
    /// <summary>What <c>GetText</c> reads of one letter.</summary>
    internal readonly record struct GlyphBox(string Value, double StartX, double EndX, double BaselineY, double PointSize);

    internal readonly record struct Word(double Left, double Right);

    /// <summary>One output line of <c>GetText</c>: its page and where its words sit, in PDF points.</summary>
    internal sealed record PhysicalLine(int Page, IReadOnlyList<Word> Words)
    {
        public double Left => Words[0].Left;

        public double Right => Words[^1].Right;

        public double FirstWordWidth => Words[0].Right - Words[0].Left;

        /// <summary>The line's smallest gap between two words; null for a one-word line or when no gap is positive.</summary>
        public double? SpaceWidth { get; } = SmallestGap(Words);

        private static double? SmallestGap(IReadOnlyList<Word> words)
        {
            double? smallest = null;
            for (var i = 1; i < words.Count; i++)
            {
                var gap = words[i].Left - words[i - 1].Right;
                if (smallest is null || gap < smallest)
                    smallest = gap;
            }

            return smallest > 0 ? smallest : null;
        }
    }

    internal static IReadOnlyList<PhysicalLine>? Read(Page page, string pageText) =>
        Align(page.Number, pageText, [.. page.Letters.Select(l => new GlyphBox(
            l.Value, l.StartBaseLine.X, l.EndBaseLine.X, l.StartBaseLine.Y, l.PointSize))]);

    /// <summary>
    /// The page's lines with their words, or null when the letters do not group into exactly the
    /// lines of <paramref name="pageText"/> or a line's characters differ from its letters'.
    /// </summary>
    internal static IReadOnlyList<PhysicalLine>? Align(int page, string pageText, IReadOnlyList<GlyphBox> letters)
    {
        var textLines = pageText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var groups = Group(letters);
        if (groups.Count != textLines.Length)
            return null;

        var lines = new List<PhysicalLine>(textLines.Length);
        for (var k = 0; k < textLines.Length; k++)
        {
            if (Words(textLines[k], groups[k]) is not { Count: > 0 } words)
                return null;

            lines.Add(new PhysicalLine(page, words));
        }

        return lines;
    }

    // GetText starts a new line at a letter whose value is not " " when IsNewline holds against the
    // previous non-whitespace letter. A space letter never starts a line.
    private static List<List<GlyphBox>> Group(IReadOnlyList<GlyphBox> letters)
    {
        var groups = new List<List<GlyphBox>> { new() };
        GlyphBox? nonWhitespacePrevious = null;
        var seenAny = false;
        foreach (var letter in letters)
        {
            if (string.IsNullOrEmpty(letter.Value))
                continue;

            if (letter.Value != " " && seenAny && nonWhitespacePrevious is { } previous && IsNewline(previous, letter))
                groups.Add([]);

            groups[^1].Add(letter);
            seenAny = true;
            if (!string.IsNullOrWhiteSpace(letter.Value))
                nonWhitespacePrevious = letter;
        }

        return groups;
    }

    // PdfPig 0.1.16 ContentOrderTextExtractor.IsNewline, on the same inputs.
    private static bool IsNewline(GlyphBox previous, GlyphBox letter)
    {
        var ptSizePrevious = (int)Math.Round(previous.PointSize);
        var ptSize = (int)Math.Round(letter.PointSize);
        var minPtSize = ptSize < ptSizePrevious ? ptSize : ptSizePrevious;
        var gap = Math.Abs(previous.BaselineY - letter.BaselineY);
        return gap > minPtSize * 0.9;
    }

    // Each non-whitespace character of the line is taken by the next letter's characters, in order; a
    // word is a run of them between whitespace in the line. Null when the two sequences differ.
    private static List<Word>? Words(string line, List<GlyphBox> group)
    {
        var stream = new List<(char C, GlyphBox Letter)>();
        foreach (var letter in group)
        {
            foreach (var c in letter.Value)
            {
                if (!char.IsWhiteSpace(c))
                    stream.Add((c, letter));
            }
        }

        var words = new List<Word>();
        var position = 0;
        GlyphBox? first = null;
        GlyphBox? last = null;
        foreach (var c in line)
        {
            if (char.IsWhiteSpace(c))
            {
                if (first is { } start && last is { } end)
                    words.Add(new Word(start.StartX, end.EndX));
                first = null;
                continue;
            }

            if (position >= stream.Count || stream[position].C != c)
                return null;

            first ??= stream[position].Letter;
            last = stream[position].Letter;
            position++;
        }

        if (first is { } lastStart && last is { } lastEnd)
            words.Add(new Word(lastStart.StartX, lastEnd.EndX));

        return position == stream.Count ? words : null;
    }
}
