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
/// and deliberately discards it. And <c>DetectedSections</c> is built in a hardcoded canonical
/// order, so it is the SAME list whatever the CV looks like — which is exactly why B1 could not see
/// the order and handed out a green light on it for two releases. The order survives in one place
/// only: the text.</para>
/// </summary>
internal static class SectionOrderAnalyzer
{
    /// <summary>
    /// The sections the CV actually has, in document order, and the order the convention recommends
    /// for them.
    /// </summary>
    internal static SectionOrderAssessment Analyze(
        string? linearText,
        CvParsingLexiconData lexicon,
        CvConventions conventions)
    {
        ArgumentNullException.ThrowIfNull(lexicon);
        ArgumentNullException.ThrowIfNull(conventions);

        var observed = Observe(linearText, lexicon);

        // OrderBy is a STABLE sort, and the stability IS the rubric's trailing "→ Övrigt": sections
        // the convention does not name keep their observed relative order and follow the named ones.
        // That is an algorithm, which is why it lives here and not as a key in the asset.
        //
        // No early return for the 0/1-section case: OrderBy of a list with one element IS that list,
        // so Deviates comes out false anyway. A guard that cannot change an outcome is not a guard —
        // it is a comment that looks like one, and a test pinning it would pass for the wrong reason.
        // The 0/1 case is carried where it MEANS something instead: SectionOrderAssessment.OrderObserved.
        var recommended = observed.OrderBy(s => RankOf(s, conventions)).ToList();

        return new SectionOrderAssessment(observed, recommended, !recommended.SequenceEqual(observed));
    }

    /// <summary>
    /// The sections present in the text, in DOCUMENT order, each carrying the heading AS THE USER
    /// WROTE IT (so a citation quotes her own words and the engine invents no vocabulary).
    ///
    /// <para><b>It runs the SEGMENTER'S detector</b> (<see cref="CvHeadingDetector"/>), not a second
    /// one. An earlier draft re-implemented detection as "normalise the whole line, look it up" —
    /// which misses the INLINE form (<c>"Kompetenser: C#, PostgreSQL"</c>, #421) the segmenter DOES
    /// parse. The section then existed in the parse and was invisible to the order, so a CV whose
    /// order genuinely deviated came back "i rekommenderad ordning" and the reorder transform stayed
    /// quiet. Observing exactly the headings the document was SEGMENTED on is the only way that
    /// cannot happen. Both review gates found this independently.</para>
    ///
    /// <para>A repeated TYPED heading denotes the same section (the segmenter concatenates those
    /// blocks), so only its FIRST position counts — without that, a CV that writes
    /// "Arbetslivserfarenhet" twice would sort as if it had two experience sections and earn a
    /// phantom reorder. FREE sections are NOT deduplicated: the segmenter deliberately keeps two
    /// same-named free sections as two sections (#815), and the evidence shows the user a list of
    /// HER OWN headings — silently collapsing two of them would make that list a lie. Two free
    /// sections rank alike and sort stably, so keeping both is correct for the ordering too.</para>
    /// </summary>
    private static List<ObservedSection> Observe(string? linearText, CvParsingLexiconData lexicon)
    {
        var observed = new List<ObservedSection>();
        if (string.IsNullOrWhiteSpace(linearText))
        {
            return observed;
        }

        var seenTyped = new HashSet<ParsedSectionKind>();

        foreach (var heading in CvHeadingDetector.Detect(linearText.Split('\n'), lexicon))
        {
            if (heading.Kind is { } kind)
            {
                if (seenTyped.Add(kind))
                {
                    observed.Add(new ObservedSection(kind, FreeId: null, heading.Heading));
                }
            }
            else
            {
                observed.Add(new ObservedSection(TypedKind: null, heading.FreeId, heading.Heading));
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
/// What the analyzer found. <see cref="Deviates"/> is the ONE definition of "the order is wrong" in
/// this codebase — B1 verdicts against it and <c>SectionReorderTransform</c> proposes against it, so
/// the two can never disagree about the same CV.
/// </summary>
internal sealed record SectionOrderAssessment(
    IReadOnlyList<ObservedSection> Observed,
    IReadOnlyList<ObservedSection> Recommended,
    bool Deviates)
{
    /// <summary>
    /// Whether the order could be OBSERVED at all — true only when the text carried at least two
    /// recognisable headings.
    ///
    /// <para><b>This is not a technicality; it is the difference between two claims.</b>
    /// <see cref="Deviates"/> is <c>false</c> both when the order was READ and found correct AND when
    /// nothing was read at all. A caller that treats the second as the first tells the user
    /// "sektionerna står i rekommenderad ordning" about a CV whose order it never saw — the §5
    /// mis-report this whole step exists to delete, reproduced inside its own fix. Both review gates
    /// found exactly that, independently. <b>Any caller making a POSITIVE statement about the order
    /// must gate on this.</b> (A caller that only ever acts on <c>Deviates == true</c> — the reorder
    /// transform — does not need it: it stays silent in both cases, which is correct.)</para>
    /// </summary>
    public bool OrderObserved => Observed.Count >= 2;

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
