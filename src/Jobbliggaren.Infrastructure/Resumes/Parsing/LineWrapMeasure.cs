using System.Buffers;
using Jobbliggaren.Application.Resumes.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Whether a line end in PDF text is a layout wrap rather than a line end the author wrote
/// (#2342). Two lines are read as one only where the text admits exactly one reading of the line
/// end, and that reading is a wrap. The width learned from the document only narrows where a
/// join is allowed; it never places one, so an ambiguous line end stays a line end.
/// </summary>
internal sealed class LineWrapMeasure
{
    // A line ending in one of these closes a sentence or opens a list; a wrap falls mid-clause.
    private static readonly SearchValues<char> Terminators = SearchValues.Create(".!?:;…");

    private LineWrapMeasure(int width) => Width = width;

    /// <summary>The longest trimmed line of the whole text, in characters.</summary>
    public int Width { get; }

    /// <summary>The measure of a text declared as PDF, the only format whose line ends can be a
    /// layout's. None for any other declared type, and none for a text with nothing in it.</summary>
    public static LineWrapMeasure? For(string? declaredContentType, string rawText)
    {
        if (!CvFileSignature.IsMediaType(declaredContentType, CvFileSignature.PdfContentType))
            return null;

        var width = 0;
        foreach (var line in rawText.Split('\n'))
            width = Math.Max(width, line.Trim().Length);

        return width > 0 ? new LineWrapMeasure(width) : null;
    }

    /// <summary>
    /// True when the trimmed line <paramref name="next"/> is the rest of what the trimmed line
    /// <paramref name="previous"/> broke off: <paramref name="next"/> opens with a lower-case
    /// letter, <paramref name="previous"/> ends mid-clause, and the first word of
    /// <paramref name="next"/> would not have fit on <paramref name="previous"/> before the measure.
    /// </summary>
    public bool Continues(string previous, string next)
    {
        if (previous.Length == 0 || next.Length == 0)
            return false;

        if (!char.IsLower(next[0]) || Terminators.Contains(previous[^1]))
            return false;

        var firstWordLength = next.Length;
        for (var i = 0; i < next.Length; i++)
        {
            if (char.IsWhiteSpace(next[i]))
            {
                firstWordLength = i;
                break;
            }
        }

        return previous.Length + 1 + firstWordLength >= Width;
    }
}
