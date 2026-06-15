namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Document-level parse confidence (OQ5), first-class on the <see cref="ParsedResume"/>
/// aggregate. The <see cref="Overall"/> level is a <b>pure, deterministic function</b>
/// of the per-section <see cref="Sections"/> verdicts (<see cref="FromSections"/>) —
/// there is no weighted float (the section verdicts ARE the confidence, mirroring
/// <c>MatchScore</c>'s no-opaque-total guard). Non-PII metadata: the evidence cites
/// structure (headings, counts), never the CV content.
/// </summary>
public sealed record ParseConfidence
{
    public OverallConfidenceLevel Overall { get; }

    public IReadOnlyList<SectionConfidence> Sections { get; }

    public ParseFallbackReason Fallback { get; }

    public ParseConfidence(
        OverallConfidenceLevel overall,
        IReadOnlyList<SectionConfidence> sections,
        ParseFallbackReason fallback)
    {
        Overall = overall;
        Sections = sections ?? [];
        Fallback = fallback;
    }

    /// <summary>
    /// True when the parse is anything other than fully confident — the UX must
    /// surface the manual-review path (OQ5). Degraded and Failed both require review.
    /// </summary>
    public bool RequiresManualReview => Overall != OverallConfidenceLevel.Confident;

    /// <summary>
    /// Deterministic document-level verdict from the section verdicts. Confident only
    /// when Contact is confident AND at least one of Experience/Education is confident
    /// (a CV without a contact section or any history is not a confident parse).
    /// No section found at all ⇒ Degraded + <see cref="ParseFallbackReason.NoSectionsDetected"/>.
    /// Extraction failure is modelled separately via <see cref="Failed"/>.
    /// </summary>
    public static ParseConfidence FromSections(IReadOnlyList<SectionConfidence> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var anyFound = false;
        foreach (var section in sections)
        {
            if (section.Level != SectionConfidenceLevel.NotFound)
            {
                anyFound = true;
                break;
            }
        }

        if (!anyFound)
        {
            return new ParseConfidence(
                OverallConfidenceLevel.Degraded, sections, ParseFallbackReason.NoSectionsDetected);
        }

        var contactConfident = LevelOf(sections, ParsedSectionKind.Contact)
            == SectionConfidenceLevel.Confident;
        var historyConfident =
            LevelOf(sections, ParsedSectionKind.Experience) == SectionConfidenceLevel.Confident
            || LevelOf(sections, ParsedSectionKind.Education) == SectionConfidenceLevel.Confident;

        var overall = contactConfident && historyConfident
            ? OverallConfidenceLevel.Confident
            : OverallConfidenceLevel.Degraded;

        return new ParseConfidence(overall, sections, ParseFallbackReason.None);
    }

    /// <summary>Extraction produced no usable text — the whole parse failed and the
    /// user is routed to manual entry. Carries no section verdicts.</summary>
    public static ParseConfidence Failed(ParseFallbackReason reason) =>
        new(OverallConfidenceLevel.Failed, [], reason);

    private static SectionConfidenceLevel LevelOf(
        IReadOnlyList<SectionConfidence> sections, ParsedSectionKind kind)
    {
        foreach (var section in sections)
        {
            if (section.Kind == kind)
            {
                return section.Level;
            }
        }

        return SectionConfidenceLevel.NotFound;
    }
}
