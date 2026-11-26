using System.Buffers;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Where one dated entry ends and the next begins in a run of lines that carries no blank line
/// (#2249). A boundary is cut only where the document admits exactly one reading of it; anything
/// read off the document may narrow where cuts are allowed, never place one. A boundary that is
/// ambiguous or unreadable is not cut, and the run stays one entry.
/// </summary>
internal static class EntryBoundaries
{
    private const int MaxHeaderLength = 120;

    // Bullet glyphs a CV realistically uses. A bulleted line is content, never a header.
    private static readonly SearchValues<char> BulletMarkers =
        SearchValues.Create(['-', '*', '•', '–', '—', '·', '●', '▪']);

    private static readonly SearchValues<char> SentenceEnds =
        SearchValues.Create(['.', '!', '?', ':', ';', ',']);

    // A comma joins two prose clauses as readily as a role and an employer, so a header that starts
    // in lower case ("iOS-utvecklare — Acme AB") is read only on these separators.
    private static readonly string[] LowercaseHeaderSeparators = [" — ", " – ", " - ", " | ", " @ "];

    private enum Layout
    {
        // "2020 – 2023" / "Role — Employer"
        PeriodFirst,

        // "Role — Employer 2020 – 2023"
        DatedHeader,

        // "Role — Employer" / "2020 – 2023"
        OneLineHeader,

        // "Role" / "Employer" / "2020 – 2023"
        TwoLineHeader,
    }

    private readonly record struct Anchor(Layout Layout, int Start, int DateLine);

    /// <summary>The entries of a split run, and the lines above its first entry.</summary>
    internal sealed record RunSplit(IReadOnlyList<string> Lead, IReadOnlyList<IReadOnlyList<string>> Entries);

    /// <summary>
    /// The run's entries, or null when it is not split: fewer than two anchors, a first anchor that
    /// admits more than one reading, or a dated line above the first anchor. A line containing
    /// <paramref name="excludedName"/> is never a header.
    /// </summary>
    internal static RunSplit? Split(IReadOnlyList<string> lines, string? excludedName)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (FirstAnchor(lines, excludedName) is not { } first)
            return null;

        for (var i = 0; i < first.Start; i++)
        {
            if (IsDated(lines[i]))
                return null;
        }

        var starts = new List<int> { first.Start };
        var previousDateLine = first.DateLine;
        for (var i = first.DateLine + 1; i < lines.Count; i++)
        {
            if (StartAt(lines, i, first.Layout, previousDateLine, excludedName) is not { } start)
                continue;

            starts.Add(start);
            previousDateLine = i;
        }

        if (starts.Count < 2)
            return null;

        var entries = new List<IReadOnlyList<string>>(starts.Count);
        for (var s = 0; s < starts.Count; s++)
        {
            var end = s + 1 < starts.Count ? starts[s + 1] : lines.Count;
            entries.Add([.. lines.Skip(starts[s]).Take(end - starts[s])]);
        }

        return new RunSplit([.. lines.Take(first.Start)], entries);
    }

    internal static bool IsBulletLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && BulletMarkers.Contains(trimmed[0]);
    }

    private static Anchor? FirstAnchor(IReadOnlyList<string> lines, string? excludedName)
    {
        var f = 0;
        while (f < lines.Count && !IsRangeRow(lines[f]) && !EndsInRange(lines[f]))
            f++;

        if (f == lines.Count)
            return null;

        if (!IsRangeRow(lines[f]))
            return IsDatedHeader(lines[f], excludedName) ? new Anchor(Layout.DatedHeader, f, f) : null;

        if (f == 0)
        {
            return lines.Count > 1 && IsCompleteHeader(lines[1], excludedName)
                ? new Anchor(Layout.PeriodFirst, 0, 0)
                : null;
        }

        if (IsCompleteHeader(lines[f - 1], excludedName))
        {
            // Two header-shaped lines over the range read as intro + "Role — Employer" and as
            // "Role" / "Employer, City" alike.
            return f >= 2 && IsHeaderShaped(lines[f - 2], excludedName)
                ? null
                : new Anchor(Layout.OneLineHeader, f - 1, f);
        }

        return f == 2
            && IsHeaderShaped(lines[0], excludedName)
            && IsHeaderShaped(lines[1], excludedName)
            && !SplitsIntoFields(lines[1])
                ? new Anchor(Layout.TwoLineHeader, 0, 2)
                : null;
    }

    // Where the entry whose date sits on line i starts, read in the run's layout — or null.
    private static int? StartAt(
        IReadOnlyList<string> lines, int i, Layout layout, int previousDateLine, string? excludedName) =>
        layout switch
        {
            Layout.DatedHeader when IsDatedHeader(lines[i], excludedName) => i,
            Layout.PeriodFirst when IsRangeRow(lines[i])
                && i - previousDateLine >= 2
                && i + 1 < lines.Count
                && IsCompleteHeader(lines[i + 1], excludedName) => i,
            Layout.OneLineHeader when IsRangeRow(lines[i])
                && i - 1 > previousDateLine
                && IsCompleteHeader(lines[i - 1], excludedName) => i - 1,
            Layout.TwoLineHeader when IsRangeRow(lines[i])
                && i - 2 > previousDateLine
                && IsHeaderShaped(lines[i - 1], excludedName)
                && !SplitsIntoFields(lines[i - 1])
                && IsHeaderShaped(lines[i - 2], excludedName) => i - 2,
            _ => null,
        };

    private static bool IsRangeRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0
            && !IsBulletLine(trimmed)
            && DatePatterns.TryStripTrailingDateRange(trimmed, out var head)
            && head.Length == 0;
    }

    private static bool EndsInRange(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0 && DatePatterns.TryStripTrailingDateRange(trimmed, out _);
    }

    private static bool IsDated(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0 && (DatePatterns.IsDateOnlyLine(trimmed) || EndsInRange(trimmed));
    }

    private static bool IsDatedHeader(string line, string? excludedName)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= MaxHeaderLength
            && !IsBulletLine(trimmed)
            && DatePatterns.TryStripTrailingDateRange(trimmed, out var head)
            && head.Length > 0
            && IsCompleteHeader(head, excludedName);
    }

    private static bool IsHeaderShaped(string line, string? excludedName)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0
            && trimmed.Length <= MaxHeaderLength
            && !IsBulletLine(trimmed)
            && !DatePatterns.IsDateOnlyLine(trimmed)
            && !SentenceEnds.Contains(trimmed[^1])
            && !trimmed.Contains(':', StringComparison.Ordinal)
            && (excludedName is null || !trimmed.Contains(excludedName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCompleteHeader(string line, string? excludedName)
    {
        var trimmed = line.Trim();
        return IsHeaderShaped(trimmed, excludedName)
            && HeadingDrivenResumeSegmenter.TrySplitFields(trimmed, out _, out _, out var separator)
            && (!char.IsLower(trimmed[0]) || LowercaseHeaderSeparators.Contains(separator));
    }

    private static bool SplitsIntoFields(string line) =>
        HeadingDrivenResumeSegmenter.TrySplitFields(line.Trim(), out _, out _, out _);
}
