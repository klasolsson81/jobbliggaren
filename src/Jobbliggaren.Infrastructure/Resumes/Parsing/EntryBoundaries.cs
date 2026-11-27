using System.Buffers;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Where one dated entry ends and the next begins in a run of lines that carries no blank line
/// (#2249). A boundary is cut only where the document admits exactly one reading of it. Two kinds of
/// evidence can make a reading unique (#2353): the vocabularies (an organisation form, a known place)
/// and the extractor's line geometry (a joined wrap, an indent). A boundary that is still ambiguous
/// or unreadable is not cut, and the run stays one entry.
/// </summary>
internal static class EntryBoundaries
{
    private const int MaxHeaderLength = 120;

    private static readonly SearchValues<char> SentenceEnds =
        SearchValues.Create(['.', '!', '?', ':', ';', ',']);

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
    internal sealed record RunSplit(IReadOnlyList<CvLine> Lead, IReadOnlyList<IReadOnlyList<CvLine>> Entries);

    /// <summary>
    /// The run's entries, or null when it is not split: fewer than two anchors, a first anchor that
    /// admits more than one reading, or a dated line above the first anchor. A line containing
    /// <paramref name="excludedName"/> is never a header.
    /// </summary>
    internal static RunSplit? Split(IReadOnlyList<CvLine> lines, string? excludedName, EntryVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (FirstAnchor(lines, excludedName, vocabulary) is not { } first)
            return null;

        for (var i = 0; i < first.Start; i++)
        {
            if (IsDated(lines[i].Text))
                return null;
        }

        var anchors = new List<Anchor> { first };
        var previousDateLine = first.DateLine;
        for (var i = first.DateLine + 1; i < lines.Count; i++)
        {
            if (StartAt(lines, i, first.Layout, previousDateLine, excludedName, vocabulary) is not { } anchor)
                continue;

            anchors.Add(anchor);
            previousDateLine = i;
        }

        if (anchors.Count < 2)
            return null;

        var entries = new List<IReadOnlyList<CvLine>>(anchors.Count);
        for (var a = 0; a < anchors.Count; a++)
        {
            var start = anchors[a].Start;
            var end = a + 1 < anchors.Count ? anchors[a + 1].Start : lines.Count;
            entries.Add([.. lines.Skip(start).Take(end - start)]);
        }

        return new RunSplit([.. lines.Take(first.Start)], entries);
    }

    // A bulleted line is content, never a header.
    private static bool IsBulletLine(string line) => CvLineShapes.IsMarkerLed(line);

    private static Anchor? FirstAnchor(IReadOnlyList<CvLine> lines, string? excludedName, EntryVocabulary vocabulary)
    {
        var f = 0;
        while (f < lines.Count && !IsRangeRow(lines[f].Text) && !EndsInRange(lines[f].Text))
            f++;

        if (f == lines.Count)
            return null;

        if (!IsRangeRow(lines[f].Text))
        {
            return IsDatedHeader(lines[f], excludedName) && HasOneReading(lines, f, -1, excludedName, vocabulary)
                ? new Anchor(Layout.DatedHeader, f, f)
                : null;
        }

        if (f == 0)
        {
            return lines.Count > 1 && IsCompleteHeader(lines[1], excludedName)
                ? new Anchor(Layout.PeriodFirst, 0, 0)
                : null;
        }

        if (EmployerAnchor(lines, f, -1, excludedName, vocabulary) is { } employer)
            return employer;

        if (IsCompleteHeader(lines[f - 1], excludedName))
        {
            return HasOneReading(lines, f - 1, -1, excludedName, vocabulary)
                ? new Anchor(Layout.OneLineHeader, f - 1, f)
                : null;
        }

        return f == 2
            && CouldHeadTheRange(lines[0], lines[1], excludedName)
            && IsHeaderShaped(lines[1], excludedName)
            && !SplitsIntoFields(lines[1].Text)
                ? new Anchor(Layout.TwoLineHeader, 0, 2)
                : null;
    }

    // Where the entry whose date sits on line i starts, read in the run's layout — or null. In a run
    // whose header sits above its date, an employer line above the date is read first, whatever the
    // run's layout (#2353 rule B). In a period-first run the line above a date ends the entry before.
    private static Anchor? StartAt(
        IReadOnlyList<CvLine> lines, int i, Layout layout, int previousDateLine, string? excludedName,
        EntryVocabulary vocabulary)
    {
        if (layout != Layout.PeriodFirst
            && IsRangeRow(lines[i].Text)
            && EmployerAnchor(lines, i, previousDateLine, excludedName, vocabulary) is { } employer)
        {
            return employer;
        }

        return layout switch
        {
            Layout.DatedHeader when IsDatedHeader(lines[i], excludedName)
                && HasOneReading(lines, i, previousDateLine, excludedName, vocabulary)
                => new Anchor(layout, i, i),
            Layout.PeriodFirst when IsRangeRow(lines[i].Text)
                && i - previousDateLine >= 2
                && i + 1 < lines.Count
                && IsCompleteHeader(lines[i + 1], excludedName)
                && (i - 1 == previousDateLine + 1 || !CouldHeadTheRange(lines[i - 1], lines[i], excludedName))
                => new Anchor(layout, i, i),
            Layout.OneLineHeader when IsRangeRow(lines[i].Text)
                && i - 1 > previousDateLine
                && IsCompleteHeader(lines[i - 1], excludedName)
                && HasOneReading(lines, i - 1, previousDateLine, excludedName, vocabulary)
                => new Anchor(layout, i - 1, i),
            // #2353 rule C: in a two-line run, "X, known place" is an organisation line too.
            Layout.TwoLineHeader when IsRangeRow(lines[i].Text)
                && i - 2 > previousDateLine
                && IsHeaderShaped(lines[i - 1], excludedName)
                && (!SplitsIntoFields(lines[i - 1].Text) || vocabulary.IsCommaPlaceLine(lines[i - 1].Text))
                && CouldHeadTheRange(lines[i - 2], lines[i - 1], excludedName)
                => new Anchor(layout, i - 2, i),
            _ => null,
        };
    }

    // #2353 (senior-cto-advisor D8, rule B and point 4 (ii)): an employer line ("Exempel AB, Göteborg")
    // directly above the range on line dateLine. A line above it that can head the range is its role,
    // and the two are a two-line header; otherwise the employer line heads the entry alone.
    private static Anchor? EmployerAnchor(
        IReadOnlyList<CvLine> lines, int dateLine, int previousDateLine, string? excludedName,
        EntryVocabulary vocabulary)
    {
        var employer = dateLine - 1;
        if (employer <= previousDateLine
            || !IsHeaderShaped(lines[employer], excludedName)
            || !vocabulary.IsEmployerLine(lines[employer].Text))
        {
            return null;
        }

        return employer - 1 > previousDateLine
            && CouldHeadTheRange(lines[employer - 1], lines[employer], excludedName)
                ? new Anchor(Layout.TwoLineHeader, employer - 1, dateLine)
                : new Anchor(Layout.OneLineHeader, employer, dateLine);
    }

    // A one-line header admits one reading only when the line above it cannot be the upper line of
    // a two-line header. Otherwise intro + "Role — Employer" and "Role" / "Employer, City" read alike.
    // A header whose second field names an organisation and whose first does not is a role and its
    // employer, so the line above is not part of it (#2353 rule A).
    private static bool HasOneReading(
        IReadOnlyList<CvLine> lines, int headerLine, int previousDateLine, string? excludedName,
        EntryVocabulary vocabulary) =>
        headerLine - 1 <= previousDateLine
        || vocabulary.IsRoleOrgLine(lines[headerLine].Text)
        || !CouldHeadTheRange(lines[headerLine - 1], lines[headerLine], excludedName);

    // #2353 rule D: a line cannot head the range below it when the extractor joined it from a wrap,
    // when it opens with a list glyph (IsHeaderShaped), or when it starts further right than the line
    // below it by more than one space, which is a list item's indent.
    private static bool CouldHeadTheRange(CvLine line, CvLine below, string? excludedName) =>
        IsHeaderShaped(line, excludedName)
        && !IsDated(line.Text)
        && !line.Provenance.Joined
        && !IsIndentedUnder(line, below);

    private static bool IsIndentedUnder(CvLine line, CvLine below) =>
        line.Provenance.Left is { } left
        && below.Provenance.Left is { } belowLeft
        && (below.Provenance.SpaceWidth ?? line.Provenance.SpaceWidth) is { } space
        && left - belowLeft > space;

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

    private static bool IsDatedHeader(CvLine line, string? excludedName)
    {
        var trimmed = line.Text.Trim();
        return DatePatterns.TryStripTrailingDateRange(trimmed, out var head)
            && head.Length > 0
            && IsHeaderShaped(ShapeText(line, head), excludedName)
            && IsCompleteHeader(line, excludedName);
    }

    // #2351 (senior-cto-advisor D5): a line the extractor joined from a layout wrap has the shape of
    // its first physical segment, which is what these predicates read before the join. Its fields
    // are read on the whole line, so the organisation is the whole second field.
    private static string ShapeText(CvLine line, string text) =>
        line.Provenance.FirstSegmentLength is { } length && length < text.Length ? text[..length] : text;

    private static bool IsHeaderShaped(CvLine line, string? excludedName) =>
        IsHeaderShaped(ShapeText(line, line.Text.Trim()), excludedName);

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

    private static bool IsCompleteHeader(CvLine line, string? excludedName)
    {
        var trimmed = line.Text.Trim();
        return IsHeaderShaped(line, excludedName)
            && CvLineShapes.TryReadHeaderFields(trimmed, out _, out _, out var separator)
            && (!char.IsLower(trimmed[0]) || CvLineShapes.ReadsLowercaseHeader(separator));
    }

    private static bool SplitsIntoFields(string line) =>
        CvLineShapes.TryReadHeaderFields(line, out _, out _, out _);
}

/// <summary>
/// The vocabularies an entry header is read against (#2353, senior-cto-advisor D8): the lexicon's
/// organisation forms and the place lexicon. The boundary reader and the field reader ask these same
/// predicates, so a line has one reading.
/// </summary>
internal readonly record struct EntryVocabulary(CvParsingLexiconData Lexicon, CvPlaceLexicon Places)
{
    /// <summary>"Exempel AB, Göteborg": the first field names an organisation and the second a place.</summary>
    internal bool IsEmployerLine(string line) =>
        CvLineShapes.TryReadHeaderFields(line, out var first, out var second, out _)
        && Lexicon.IsOrganisation(first)
        && ContactPatterns.IsBarePlace(second, Places);

    /// <summary>"Testare — Exempel AB": the second field names an organisation and the first does not.</summary>
    internal bool IsRoleOrgLine(string line) =>
        CvLineShapes.TryReadHeaderFields(line, out var first, out var second, out _)
        && Lexicon.IsOrganisation(second)
        && !Lexicon.IsOrganisation(first);

    /// <summary>"Exempel Konsult, Göteborg": a comma and a known place after it.</summary>
    internal bool IsCommaPlaceLine(string line) =>
        CvLineShapes.TryReadHeaderFields(line, out _, out var second, out var separator)
        && separator == ", "
        && ContactPatterns.IsBarePlace(second, Places);

    /// <summary>A line the vocabularies read as an organisation: one that names an organisation
    /// ("Exempel AB", "Exempel AB, Göteborg"), or "X, known place".</summary>
    internal bool IsOrganisationLine(string line) => Lexicon.IsOrganisation(line) || IsCommaPlaceLine(line);
}
