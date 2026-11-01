using System.Buffers;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Where one dated entry ends and the next begins inside a run of lines that carries NO blank line
/// (#2249). A blank line is the only boundary <see cref="HeadingDrivenResumeSegmenter"/> used to
/// see, and two common inputs never have one: PDF text (the extractor emits one newline per visual
/// line, so authored spacing is gone — pinned by
/// <c>PdfPigOpenXmlCvTextExtractorTests.Extract_PdfSpacingIsInvisibleInTheExtractedText_TodaysBase</c>)
/// and a DOCX written with paragraph spacing instead of empty paragraphs. Both collapsed a whole
/// experience section into one entry.
///
/// <para><b>The signal is the entry's own date, never spacing.</b> Two shapes are recognised:
/// a date ROW (a line carrying nothing but a period, <see cref="DatePatterns.IsDateOnlyLine"/>)
/// with the header lines that sit above it, and a DATED HEADER (role/employer text ending in a date
/// RANGE on the same line). A run is split only when it holds at least TWO anchors of one shape — a
/// run with one dated entry stays exactly what it was, so every blank-line-separated CV is
/// untouched.</para>
///
/// <para><b>How many header lines go with a date row is read off the document, not assumed.</b>
/// The first entry in the run fixes it (0 when the period comes first, 1 for "Role — Employer",
/// 2 for "Role / Employer / Period"), and every later entry is cut at the same height. A later
/// entry whose header lines do not look like a header is not cut at all — two entries left fused
/// is the honest failure, an entry cut through its description is not.</para>
///
/// <para><b>What does not count.</b> A year or range INSIDE prose ("lanserades i oktober 2024",
/// "During 2024-2025 I …") is neither a date row nor a trailing range; a bulleted line, a line that
/// starts in lower case (a wrapped continuation) and a line longer than any header never anchor an
/// entry. Nothing is added, dropped or reordered: the entries partition the input lines.</para>
/// </summary>
internal static class EntryBoundaries
{
    // A header names a role, an employer and perhaps a place and a period. A line longer than this
    // is wrapped description prose, whatever it ends with.
    private const int MaxHeaderLength = 120;

    // The most header lines a date row may own ("Role / Employer / Period" is two).
    private const int MaxHeaderHeight = 2;

    // Bullet glyphs a CV realistically uses. A bulleted line is content, never a header.
    private static readonly SearchValues<char> BulletMarkers =
        SearchValues.Create(['-', '*', '•', '–', '—', '·', '●', '▪']);

    // A line ending like a sentence is prose, not the upper line of a two-line header.
    private static readonly SearchValues<char> SentenceEnds =
        SearchValues.Create(['.', '!', '?', ':', ';', ',']);

    /// <summary>
    /// Splits a run of non-blank lines into entries. Returns the run whole when it does not carry
    /// two anchors of one shape. Lines before the first anchor (an intro sentence) stay together as
    /// an entry of their own rather than being glued to the first dated entry.
    /// </summary>
    internal static List<IReadOnlyList<string>> Split(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var starts = DateRowStarts(lines) ?? DatedHeaderStarts(lines);
        if (starts is null)
            return [lines];

        if (starts[0] > 0)
            starts.Insert(0, 0);

        var entries = new List<IReadOnlyList<string>>(starts.Count);
        for (var s = 0; s < starts.Count; s++)
        {
            var end = s + 1 < starts.Count ? starts[s + 1] : lines.Count;
            entries.Add([.. lines.Skip(starts[s]).Take(end - starts[s])]);
        }

        return entries;
    }

    /// <summary>
    /// True when <paramref name="line"/> (followed by <paramref name="next"/>) opens a dated entry:
    /// a dated header, a header line over a date row, or a date row over a header line. Read by
    /// <see cref="CvHeadingDetector"/> to tell an inline "Experience: …" heading that opens a list
    /// of entries from a profile sentence that merely starts with the same word.
    /// </summary>
    internal static bool OpensEntry(string line, string? next) =>
        IsDatedHeader(line)
        || (next is not null && CanCarryHeader(line) && IsDateRow(next))
        || (next is not null && IsDateRow(line) && CanCarryHeader(next));

    internal static bool IsBulletLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && BulletMarkers.Contains(trimmed[0]);
    }

    private static List<int>? DateRowStarts(IReadOnlyList<string> lines)
    {
        var rows = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsDateRow(lines[i]))
                rows.Add(i);
        }

        if (rows.Count < 2)
            return null;

        var height = HeaderHeight(lines, rows[0]);
        var starts = new List<int> { rows[0] - height };
        var previousRow = rows[0];

        foreach (var row in rows.Skip(1))
        {
            // A date row directly under another one ("2010 – 2013" / "2014") continues that entry.
            if (row - previousRow > 1 && StartFor(lines, row, height) is { } start && start > previousRow)
                starts.Add(start);

            previousRow = row;
        }

        return starts.Count >= 2 ? starts : null;
    }

    private static List<int>? DatedHeaderStarts(IReadOnlyList<string> lines)
    {
        var starts = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsDatedHeader(lines[i]))
                starts.Add(i);
        }

        return starts.Count >= 2 ? starts : null;
    }

    // How many lines above the FIRST date row are its header. The line directly above is the header
    // whenever it can be one; a second line counts only when both read as header lines.
    private static int HeaderHeight(IReadOnlyList<string> lines, int firstRow)
    {
        if (firstRow == 0 || !CanCarryHeader(lines[firstRow - 1]))
            return 0;

        var height = 1;
        while (height < MaxHeaderHeight
            && firstRow - height - 1 >= 0
            && IsStrictHeader(lines[firstRow - height])
            && IsStrictHeader(lines[firstRow - height - 1]))
        {
            height++;
        }

        return height;
    }

    // Where the entry owning the date row at <paramref name="row"/> starts, at the learned height —
    // or null when the line above cannot be its header.
    private static int? StartFor(IReadOnlyList<string> lines, int row, int height)
    {
        if (height == 0)
            return row;

        if (!CanCarryHeader(lines[row - 1]))
            return null;

        var start = row - 1;
        while (row - start < height && start - 1 >= 0 && IsStrictHeader(lines[start - 1]))
            start--;

        return start;
    }

    private static bool IsDateRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0 && !IsBulletLine(trimmed) && DatePatterns.IsDateOnlyLine(trimmed);
    }

    private static bool IsDatedHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= MaxHeaderLength
            && !IsBulletLine(trimmed)
            && DatePatterns.TryStripTrailingDateRange(trimmed, out var head)
            && head.Length > 0
            && !char.IsLower(head[0]);
    }

    private static bool CanCarryHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0
            && trimmed.Length <= MaxHeaderLength
            && !char.IsLower(trimmed[0])
            && !IsBulletLine(trimmed)
            && !IsDateRow(trimmed);
    }

    private static bool IsStrictHeader(string line)
    {
        var trimmed = line.Trim();
        return CanCarryHeader(trimmed) && !SentenceEnds.Contains(trimmed[^1]);
    }
}
