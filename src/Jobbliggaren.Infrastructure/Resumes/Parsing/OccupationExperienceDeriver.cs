using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// The import-time per-occupation experience attribution pass (ADR 0079-amendment,
/// exp-per-occ PR-2 — deterministic, NO AI/LLM). Reuses the shipped, group-spread-gated
/// <see cref="IOccupationCodeDeriver"/> (DRY/OCP — its union <c>DeriveManyAsync</c> is
/// untouched) to re-derive each experience entry's SSYK-4 group(s), and the promoted
/// <see cref="PeriodParser"/> to parse each entry's period, then aggregates per group as the
/// merged-interval union of the contributing spans (Klas-val: "lifetime in the field").
/// </summary>
internal sealed class OccupationExperienceDeriver(
    IOccupationCodeDeriver occupationDeriver,
    IDateTimeProvider clock) : IOccupationExperienceDeriver
{
    public async ValueTask<IReadOnlyDictionary<string, int>> DeriveApproximateYearsAsync(
        IReadOnlyList<ParsedExperience> experiences, CancellationToken cancellationToken)
    {
        // Collect every contributing experience span per SSYK-4 group. The clock resolves an
        // ongoing role's "present" end to the current year (IDateTimeProvider — never
        // DateTime.Now, CLAUDE.md §5).
        var spansByGroup = new Dictionary<string, List<(int Start, int End)>>(StringComparer.Ordinal);
        var currentYear = clock.UtcNow.Year;

        foreach (var experience in experiences)
        {
            // No parseable period (free-text, missing, or a malformed reverse range) → the entry
            // contributes nothing; the group it would map to stays "not stated" unless another
            // entry supplies a span (honest, §5 — never a fabricated number).
            if (!PeriodParser.TryParseYearSpan(experience.Period, currentYear, out var startYear, out var endYear))
                continue;

            var sources = EntrySources(experience);
            if (sources.Count == 0)
                continue;

            // Re-derive THIS entry's group(s) via the SAME gated deriver the union pass used, so
            // the join on concept-id is exact (the union pass discards the entry→group link; this
            // re-establishes it per-entry without modifying DeriveManyAsync — OCP).
            var derivation = await occupationDeriver.DeriveManyAsync(sources, cancellationToken);
            foreach (var candidate in derivation.Candidates)
            {
                if (!spansByGroup.TryGetValue(candidate.OccupationGroupConceptId, out var spans))
                    spansByGroup[candidate.OccupationGroupConceptId] = spans = [];
                spans.Add((startYear, endYear));
            }
        }

        // Merge each group's spans into a non-overlapping union and cap at the human-range
        // bound (SPOT — the same MaxExperienceYears the write-path invariant enforces, so a
        // CV-derived seed can never exceed what the user could later confirm).
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (conceptId, spans) in spansByGroup)
            result[conceptId] = Math.Min(MergedSpanYears(spans), MatchPreferences.MaxExperienceYears);

        return result;
    }

    // The entry's occupation-bearing strings (Title + Organization, parity with the import
    // union source-builder): the layout-naive parser may put the role in either slot, and the
    // deriver self-filters companies/schools to no match.
    private static List<string> EntrySources(ParsedExperience experience)
    {
        var sources = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(experience.Title))
            sources.Add(experience.Title.Trim());
        if (!string.IsNullOrWhiteSpace(experience.Organization))
            sources.Add(experience.Organization.Trim());
        return sources;
    }

    // Merged-interval union (Klas-val, ADR 0079-amendment): total covered duration of the
    // group's spans, never double-counting overlapping/concurrent roles. Each span contributes
    // (endYear - startYear) at year granularity (CTO C-1); two concurrent roles in the same
    // group collapse to one interval. Deterministic: sort by start, then end.
    private static int MergedSpanYears(List<(int Start, int End)> spans)
    {
        spans.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

        var total = 0;
        var currentStart = spans[0].Start;
        var currentEnd = spans[0].End;

        for (var i = 1; i < spans.Count; i++)
        {
            var (start, end) = spans[i];
            if (start <= currentEnd)
            {
                // Overlapping or adjacent — extend the open run.
                if (end > currentEnd)
                    currentEnd = end;
            }
            else
            {
                // Disjoint — close the run and start a new one.
                total += currentEnd - currentStart;
                currentStart = start;
                currentEnd = end;
            }
        }

        total += currentEnd - currentStart;
        return total;
    }
}
