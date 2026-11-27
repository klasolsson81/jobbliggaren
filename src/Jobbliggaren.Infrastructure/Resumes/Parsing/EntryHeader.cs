namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Which of an entry's lines is its header line (#2353, senior-cto-advisor D11): the first line, or
/// the second when the first carries nothing but a period (#1060 β-1, a template that renders the
/// period cell before the role cell). The field reader splits this line and the description starts
/// after it, so the two read the same line as the header.
/// </summary>
internal static class EntryHeader
{
    /// <summary>The index of the header line among the entry's trimmed, non-empty lines.</summary>
    internal static int LineIndex(IReadOnlyList<string> lines) =>
        lines.Count >= 2 && DatePatterns.StripTrailingDate(lines[0]).Length == 0 ? 1 : 0;
}
