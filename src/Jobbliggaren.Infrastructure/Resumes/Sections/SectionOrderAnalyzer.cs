using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Sections;

/// <summary>
/// "What is this CV's section order, and does it deviate from the recommendation?" — asked by the
/// REVIEW engine (B1) and the IMPROVEMENT engine (<c>SectionReorderTransform</c>), answered in ONE
/// place (Fas 4b 8b.4b, ADR 0108).
///
/// <para><b>Why it is shared and not duplicated.</b> The observed order is one knowledge piece
/// (Hunt/Thomas 1999, DRY). If the improvement engine owned this computation and the review engine
/// grew its own, the two would drift — and the product would tell the user, on one screen, that her
/// order is fine and, on another, that it should change. That is the exact fork class this whole
/// step exists to remove (ADR 0107 §3), so the analyzer is extracted while there is still only one
/// implementation to extract.</para>
///
/// <para><b>Pure and total.</b> No I/O, no state, no clock — a function of
/// (linear text, lexicon, conventions). Both engines are stateless singletons and both call it once
/// per CV.</para>
///
/// <para><b>It reads the linear text, not the parse artifact — because the artifact cannot hold the
/// answer.</b> The six TYPED sections are separate properties on <c>ParsedResumeContent</c> (no
/// ordinal at all) and only the FREE sections are a list; the segmenter knows each heading's line
/// (<c>HeadingHit(int Line, …)</c>) and deliberately discards it. And <c>DetectedSections</c> is
/// built in a hardcoded canonical order, so it is the SAME list whatever the CV looks like — which
/// is exactly why B1 could not see the order and handed out a green light on it for two releases.
/// The order survives in one place only: the text.</para>
/// </summary>
internal static class SectionOrderAnalyzer
{
    /// <summary>
    /// The sections the CV actually has, in document order, and the order the convention
    /// recommends for them.
    /// </summary>
    internal static SectionOrderAssessment Analyze(
        string? linearText,
        CvParsingLexiconData lexicon,
        CvConventions conventions)
    {
        ArgumentNullException.ThrowIfNull(lexicon);
        ArgumentNullException.ThrowIfNull(conventions);

        var observed = Observe(linearText, lexicon);

        // Fewer than two recognised sections cannot BE out of order. Saying otherwise about a CV
        // whose structure we cannot see would be a verdict without evidence (ADR 0074 Invariant 2).
        if (observed.Count < 2)
        {
            return new SectionOrderAssessment(observed, observed, Deviates: false);
        }

        // OrderBy is a STABLE sort, and the stability IS the rubric's trailing "→ Övrigt": sections
        // the convention does not name keep their observed relative order and follow the named ones.
        // That is an algorithm, which is why it lives here and not as a key in the asset.
        var recommended = observed.OrderBy(s => RankOf(s, conventions)).ToList();

        return new SectionOrderAssessment(observed, recommended, !recommended.SequenceEqual(observed));
    }

    /// <summary>
    /// The sections present in the text, in DOCUMENT order, each carrying the heading AS THE USER
    /// WROTE IT (so a citation quotes her own words and the engine invents no vocabulary). A heading
    /// that repeats denotes the SAME section — the segmenter concatenates such blocks — so only its
    /// FIRST position counts.
    /// </summary>
    private static List<ObservedSection> Observe(string? linearText, CvParsingLexiconData lexicon)
    {
        var observed = new List<ObservedSection>();
        if (string.IsNullOrWhiteSpace(linearText))
        {
            return observed;
        }

        var seenTyped = new HashSet<ParsedSectionKind>();
        var seenFree = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in linearText.Split('\n'))
        {
            // The lexicon's OWN normaliser — the same one the segmenter runs. A caller-side
            // normalisation could drift from it, and the drift would show up as headings that
            // segment correctly but order as if they were absent.
            var lexicalKey = CvParsingLexiconLoader.NormalizeHeading(line);
            if (lexicalKey.Length == 0)
            {
                continue;
            }

            if (lexicon.HeadingMap.TryGetValue(lexicalKey, out var kind))
            {
                if (seenTyped.Add(kind))
                {
                    observed.Add(new ObservedSection(kind, FreeId: null, line.Trim()));
                }
            }
            else if (lexicon.FreeSectionIdByHeading.TryGetValue(lexicalKey, out var freeId)
                     && seenFree.Add(freeId))
            {
                observed.Add(new ObservedSection(TypedKind: null, freeId, line.Trim()));
            }
        }

        return observed;
    }

    /// <summary>
    /// The section's position in the recommended order; <see cref="int.MaxValue"/> when the
    /// convention does not name it (a free section — it sorts after the named ones, stably).
    /// </summary>
    private static int RankOf(ObservedSection section, CvConventions conventions)
    {
        for (var i = 0; i < conventions.SectionOrder.Count; i++)
        {
            var entry = conventions.SectionOrder[i];

            var isMatch = section.TypedKind is not null
                ? entry.TypedKind == section.TypedKind
                : entry.TypedKind is null
                    && string.Equals(entry.SectionId, section.FreeId, StringComparison.Ordinal);

            if (isMatch)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}

/// <summary>
/// What the analyzer found. <paramref name="Deviates"/> is the ONE definition of "the order is
/// wrong" in this codebase — B1 verdicts against it and <c>SectionReorderTransform</c> proposes
/// against it, so the two can never disagree with each other about the same CV.
/// </summary>
internal sealed record SectionOrderAssessment(
    IReadOnlyList<ObservedSection> Observed,
    IReadOnlyList<ObservedSection> Recommended,
    bool Deviates)
{
    /// <summary>The observed headings, in the user's own words: "Utbildning, Arbetslivserfarenhet".</summary>
    public string ObservedHeadings => Join(Observed);

    /// <summary>The recommended headings, same words, reordered.</summary>
    public string RecommendedHeadings => Join(Recommended);

    private static string Join(IEnumerable<ObservedSection> sections) =>
        string.Join(", ", sections.Select(s => s.Heading));
}

/// <summary>One recognised section: its identity (typed OR free, never both) and the heading the
/// user wrote for it.</summary>
internal readonly record struct ObservedSection(
    ParsedSectionKind? TypedKind,
    string? FreeId,
    string Heading);
