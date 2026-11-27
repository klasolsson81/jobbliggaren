using System.Buffers;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// The ONE owner of the shapes a CV line is read by (#2353, finding 3): the glyphs that lead a list
/// item, the separators an entry header splits on, and the reader that splits it. The segmenter, the
/// entry-boundary reader, B5 and the PDF wrap join read these members instead of keeping copies.
/// FORM, so it lives in C# and not in the lexicon (ADR 0108 §2).
/// </summary>
internal static class CvLineShapes
{
    // A new code point is added only when an exporter is measured emitting it. Google Docs emits
    // ● and Word emits • in a PDF's text layer (measured 2026-11-27); both were already here.
    private static readonly char[] MarkerGlyphs =
        ['•', '◦', '‣', '·', '●', '○', '▪', '▫', '■', '–', '—', '-', '*'];

    private static readonly SearchValues<char> Markers = SearchValues.Create(MarkerGlyphs);

    private readonly record struct HeaderSeparator(string Token, bool JoinsProse);

    // Read in this order, not by position in the line, so "Role — Employer, City" splits on the dash.
    // A separator that also joins two prose clauses reads a header only when it starts upper-case.
    private static readonly HeaderSeparator[] HeaderSeparators =
    [
        new(" — ", JoinsProse: false),
        new(" – ", JoinsProse: false),
        new(" - ", JoinsProse: false),
        new(", ", JoinsProse: true),
        new(" | ", JoinsProse: false),
        new(" @ ", JoinsProse: false),
        new(" at ", JoinsProse: true),
        new(" på ", JoinsProse: true),
        new(" hos ", JoinsProse: true),
    ];

    /// <summary>The list-item glyphs, for a caller that composes them with another set.</summary>
    internal static IReadOnlyList<char> MarkerGlyphList => MarkerGlyphs;

    internal static bool IsMarker(char c) => Markers.Contains(c);

    /// <summary>The line's first non-space character is a list-item glyph.</summary>
    internal static bool IsMarkerLed(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && Markers.Contains(trimmed[0]);
    }

    /// <summary>The line opens with a list-item glyph followed by whitespace.</summary>
    internal static bool TryLeadMarker(string line, out char marker)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length >= 2 && Markers.Contains(trimmed[0]) && char.IsWhiteSpace(trimmed[1]))
        {
            marker = trimmed[0];
            return true;
        }

        marker = default;
        return false;
    }

    /// <summary>A header that starts in lower case is read only on a separator that never joins prose.</summary>
    internal static bool ReadsLowercaseHeader(string separator) =>
        HeaderSeparators.Any(s => !s.JoinsProse && s.Token == separator);

    /// <summary>
    /// Splits an entry header into its two fields on the first separator in table order. A trailing
    /// date is stripped first (<see cref="DatePatterns.StripTrailingDate"/>).
    /// </summary>
    internal static bool TryReadHeaderFields(
        string line, out string title, out string organization, out string separator)
    {
        var text = DatePatterns.StripTrailingDate(line.Trim());
        foreach (var candidate in HeaderSeparators)
        {
            var index = text.IndexOf(candidate.Token, StringComparison.OrdinalIgnoreCase);
            if (index > 0 && index + candidate.Token.Length < text.Length)
            {
                title = text[..index].Trim();
                organization = text[(index + candidate.Token.Length)..].Trim();
                separator = candidate.Token;
                return true;
            }
        }

        title = organization = separator = string.Empty;
        return false;
    }
}
