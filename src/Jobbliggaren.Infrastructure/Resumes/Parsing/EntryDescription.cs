namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// An entry's description lines (#2353, senior-cto-advisor D11): the entry's block without its
/// header line (<see cref="EntryHeader"/>), the lines above it, its period lines and its
/// organisation line, each line trimmed and otherwise verbatim. The segmenter stores them as the
/// entry's description, and the review reads the same lines from an entry stored before the
/// segmenter did (#487 review side, #534 improve side).
/// </summary>
internal static class EntryDescription
{
    internal static IReadOnlyList<string> Lines(string entryText, string? organization)
    {
        ArgumentNullException.ThrowIfNull(entryText);

        var lines = entryText
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var description = new List<string>(lines.Count);
        for (var i = EntryHeader.LineIndex(lines) + 1; i < lines.Count; i++)
        {
            var line = lines[i];

            // A line that is purely the period ("2013–2021", "01/2022 – nuvarande") is not a bullet.
            // A UNION of two predicates, deliberately not one (#1060 β-3 follow-up): neither subsumes
            // the other, in either direction. The axes live behind DatePatterns.IsDateOnlyLine's
            // docblock, which names DatePatternsDateOnlyLineTests as the adjudicating InlineData;
            // ReviewTextPeriodLineUnionTests and DateModelWideningReviewSideTests pin what a
            // released date row would do to the review.
            if (PeriodParser.TryParse(line, out _, out _, out _) || DatePatterns.IsDateOnlyLine(line))
                continue;

            // The organisation on its own line (the "Title\nCompany\nDates" layout) is not a bullet.
            if (!string.IsNullOrWhiteSpace(organization)
                && string.Equals(line, organization, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            description.Add(line);
        }

        return description;
    }
}
