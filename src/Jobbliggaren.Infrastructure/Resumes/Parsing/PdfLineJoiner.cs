using System.Buffers;
using System.Text;
using Jobbliggaren.Application.Resumes.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Turns a PDF's layout wraps back into the author's lines (#2353, senior-cto-advisor D4, column edge
/// per its 2026-11-27 point 1). A line end is a layout wrap when the next line's first word, plus the
/// line's own smallest space, would not have fitted before the column's edge, which is the document's
/// furthest line end. Nothing is tuned. A wrap becomes one space only when, in addition:
/// <list type="bullet">
/// <item>the two lines are on one page and overlap horizontally;</item>
/// <item>the next line does not open with a list-item glyph and a space;</item>
/// <item>the next line opens with a lower-case letter and the line has no terminator at its end, or
/// (#2349) the line belongs to a list item that leads with a glyph and a hanging indent, and the next
/// line starts at that item's text indent, within the item's own space width.</item>
/// </list>
/// Every other line end is the author's. The text keeps every character: only a joined seam's
/// <c>'\n'</c> becomes <c>' '</c>.
/// </summary>
internal static class PdfLineJoiner
{
    private static readonly SearchValues<char> Terminators = SearchValues.Create(".!?:;…");

    internal sealed record Joined(string Text, CvTextProvenance Provenance);

    /// <summary>A list item's text indent and its first line's space width (#2349).</summary>
    internal readonly record struct ListItem(double TextIndent, double SpaceWidth);

    /// <summary>
    /// <paramref name="text"/> is the extracted text; <paramref name="lines"/> are its physical lines in
    /// the same order. Null when the two do not index each other.
    /// </summary>
    internal static Joined? Join(string text, IReadOnlyList<PdfPageLines.PhysicalLine> lines)
    {
        var textLines = text.Split('\n');
        if (lines.Count == 0 || textLines.Length != lines.Count)
            return null;

        var edge = lines.Max(l => l.Right);
        var builder = new StringBuilder(text.Length);
        var provenance = new List<CvLineProvenance>();
        var first = 0;
        var item = ItemOf(lines[0], textLines[0]);
        builder.Append(textLines[0]);
        for (var i = 1; i < lines.Count; i++)
        {
            if (IsWrap(lines[i - 1], textLines[i - 1], lines[i], textLines[i], edge, item))
            {
                builder.Append(' ').Append(textLines[i]);
                continue;
            }

            provenance.Add(Provenance(lines[first], textLines[first], joined: i - first > 1));
            builder.Append('\n').Append(textLines[i]);
            first = i;
            item = ItemOf(lines[i], textLines[i]);
        }

        provenance.Add(Provenance(lines[first], textLines[first], joined: lines.Count - first > 1));
        var result = builder.ToString();
        return new Joined(result, CvTextProvenance.For(result, provenance));
    }

    internal static bool IsWrap(
        PdfPageLines.PhysicalLine previous, string previousText,
        PdfPageLines.PhysicalLine next, string nextText,
        double edge, ListItem? item = null)
    {
        if (previous.Page != next.Page || previous.SpaceWidth is not { } space)
            return false;

        var didNotFit = previous.Right + space + next.FirstWordWidth > edge;
        var overlaps = previous.Left < next.Right && next.Left < previous.Right;
        if (!didNotFit || !overlaps || CvLineShapes.TryLeadMarker(nextText, out _))
            return false;

        // #2349: a forced wrap inside a list item whose next line sits at the item's text indent
        // continues the item, whatever it opens with. A line at the glyph's indent is a new item.
        if (item is { } listItem && Math.Abs(next.Left - listItem.TextIndent) <= listItem.SpaceWidth)
            return true;

        var opening = nextText.TrimStart();
        var ending = previousText.TrimEnd();
        return opening.Length > 0
            && char.IsLower(opening[0])
            && ending.Length > 0
            && !Terminators.Contains(ending[^1]);
    }

    // A line that opens with a list-item glyph and a space, and has a word after it, starts an item
    // whose text begins at that word.
    internal static ListItem? ItemOf(PdfPageLines.PhysicalLine line, string text) =>
        CvLineShapes.TryLeadMarker(text, out _) && line.Words.Count >= 2 && line.SpaceWidth is { } space
            ? new ListItem(line.Words[1].Left, space)
            : null;

    // What the segmenter reads of a line is its first physical segment.
    private static CvLineProvenance Provenance(PdfPageLines.PhysicalLine first, string firstText, bool joined) =>
        new(joined ? firstText.Trim().Length : null, first.Left, first.SpaceWidth);
}
