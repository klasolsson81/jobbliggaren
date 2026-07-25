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

        return new SectionOrderAssessment(
            observed, recommended, !recommended.SequenceEqual(observed),
            CoreLeadInOf(observed, conventions));
    }

    /// <summary>
    /// How far into the document the reader must go before meeting the FIRST core section, and what
    /// stands ahead of it (#890) — the MEASURE behind the rubric's "kreativ ordning som döljer
    /// kärninfo", with no verdict attached.
    ///
    /// <para><b>Lead-in, not displacement, and the difference cost a re-bind.</b> The first design
    /// counted, per core section, the sections above it that the convention ranks AFTER it. That
    /// measure treated <see cref="int.MaxValue"/> — which means "this convention has no opinion about
    /// where this section goes" — as the strongest possible positional claim, so every unranked
    /// section ("Certifieringar", "Legitimation", "Projekt") counted as burial by construction. It
    /// reduced to the absolute-depth measure the bind had explicitly rejected (exactly so for
    /// <c>contact</c>, whose rank is 0), and it FAILED real CVs: the Swedish healthcare CV that leads
    /// with Legitimation, the IT CV with Certifieringar and Kurser, the portfolio CV with several
    /// project sections, and — worst — a CV built from two layouts the bind itself classified as
    /// acceptable (education-first plus competence-and-language-first), where the evidence sentence
    /// named her own Utbildning section as one of the things burying her experience.</para>
    ///
    /// <para><b>The measure is now position only.</b> Rank plays no part; the only knowledge consumed
    /// is the core/non-core partition, which the asset sources. That is what makes it able to separate
    /// position from presence: <c>Kontakt, Profil, Kompetenser, Språk, Intressen, Erfarenhet,
    /// Utbildning</c> and <c>Profil, Kompetenser, Språk, Intressen, Erfarenhet, Utbildning, Kontakt</c>
    /// hold the SAME seven sections with one heading moved, and only the second is buried.</para>
    ///
    /// <para><b>It refuses to measure unless every core section was observed, and that precondition is
    /// a proof rather than padding.</b> An unlocated core section may sit at position 0 — an unheaded
    /// contact block at the top of the page is the COMMON Swedish layout, not an edge case — so a
    /// lead-in computed over an incomplete core set can only ever OVER-count, and the bias is unbounded
    /// on a section-rich CV. Refusing is the honest answer; the criterion still Warns on deviation.</para>
    ///
    /// <para><b>The known, accepted false positive, named rather than left to be discovered:</b> a CV
    /// whose contact block sits unheaded at the top AND that also carries a headed "Kontakt" section
    /// far down (a repeated contact block, or a "Kontakta mig"-style footer) measures a long lead-in,
    /// because the measure can only see headings. The core set is complete, so the precondition does
    /// not save it. It is rare, it is bounded, and it produces a Fail on a document that is arguably
    /// fine — so it is written here instead of being met as a surprise.</para>
    ///
    /// <para><b>The measure lives here; the threshold does not.</b> This analyzer is shared with
    /// <c>SectionReorderTransform</c> — that sharing is the class's entire thesis — and the improvement
    /// engine has no business holding a review threshold. So the count is computed here and
    /// <c>B1SectionsRule</c> compares it against rubric data. (D3 has the same measure/threshold split;
    /// it differs in that D3 also consumes its RECOMMENDATION data in the rule, while the core/non-core
    /// partition has to be consumed here — a lead-in is undefined without knowing what counts as core.)</para>
    /// </summary>
    private static CoreLeadIn? CoreLeadInOf(
        List<ObservedSection> observed, CvConventions conventions)
    {
        var observedCoreIds = new HashSet<string>(StringComparer.Ordinal);
        var firstCoreIndex = -1;

        for (var i = 0; i < observed.Count; i++)
        {
            if (!TryResolveCoreId(observed[i], conventions, out var coreId))
                continue;

            observedCoreIds.Add(coreId);

            if (firstCoreIndex < 0)
                firstCoreIndex = i;
        }

        // The validity precondition. The loader guarantees coreSections carries no duplicates, so a
        // count comparison is a complete check.
        if (firstCoreIndex < 0 || observedCoreIds.Count != conventions.CoreSections.Count)
            return null;

        return new CoreLeadIn(observed[firstCoreIndex], observed.Take(firstCoreIndex).ToList());
    }

    /// <summary>
    /// Where the convention places this section, if it names it at all. ONE matcher, two readers
    /// (<see cref="RankOf"/> and <see cref="TryResolveCoreId"/>) — a second copy of the identity
    /// predicate would let a section be "core" by one match and take its rank from another entry,
    /// which is the two-normalisers defect class this lane spent #898 removing.
    /// </summary>
    private static bool TryResolveEntry(
        ObservedSection section, CvConventions conventions, out int index)
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
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    /// <summary>The section's core id, when the convention names it AND calls it core. The loader
    /// pins every core id into <c>sectionOrder</c>, so an id that cannot be resolved here is a data
    /// error that already failed at host build, never a silent miss.</summary>
    private static bool TryResolveCoreId(
        ObservedSection section, CvConventions conventions, out string coreId)
    {
        if (TryResolveEntry(section, conventions, out var index))
        {
            var candidate = conventions.SectionOrder[index].SectionId;
            if (conventions.CoreSections.Contains(candidate))
            {
                coreId = candidate;
                return true;
            }
        }

        coreId = string.Empty;
        return false;
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
    private static int RankOf(ObservedSection section, CvConventions conventions) =>
        TryResolveEntry(section, conventions, out var index) ? index : int.MaxValue;
}

/// <summary>
/// What the analyzer found. <see cref="Deviates"/> is the ONE definition of "the order is wrong" in
/// this codebase — B1 verdicts against it and <c>SectionReorderTransform</c> proposes against it, so
/// the two can never disagree about the same CV.
/// </summary>
internal sealed record SectionOrderAssessment(
    IReadOnlyList<ObservedSection> Observed,
    IReadOnlyList<ObservedSection> Recommended,
    bool Deviates,
    CoreLeadIn? CoreLeadIn)
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

/// <summary>
/// The FIRST core section and everything the reader meets before it (#890) — present only when every
/// core section was observed, because a lead-in over an incomplete core set can only over-count.
///
/// <para>The preceding sections are carried, not merely counted, because the verdict that reads this
/// must cite them in the user's own headings — a count on its own is an opaque number attached to a
/// judgement, which is the §5 sin this criterion is being sharpened to avoid, not to commit.</para>
/// </summary>
internal sealed record CoreLeadIn(
    ObservedSection Section,
    IReadOnlyList<ObservedSection> Preceding)
{
    /// <summary>How many sections stand ahead of the first core section. A MEASURE, never a verdict —
    /// <c>B1SectionsRule</c> owns the threshold that turns it into one.</summary>
    public int Count => Preceding.Count;

    /// <summary>The first core section's heading, as the user wrote it.</summary>
    public string Heading => Section.Heading;

    /// <summary>The preceding sections' headings, in document order: "Profil, Kompetenser".</summary>
    public string PrecedingHeadings => string.Join(", ", Preceding.Select(p => p.Heading));
}

/// <summary>One recognised section: its identity (typed OR free, never both) and the heading the
/// user wrote for it.</summary>
internal readonly record struct ObservedSection(
    ParsedSectionKind? TypedKind,
    string? FreeId,
    string Heading);
