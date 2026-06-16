namespace Jobbliggaren.QA.Corpus.Generation;

/// <summary>
/// Edge-strata for the stress corpus. The quota table (see <see cref="CorpusConfig"/>)
/// deliberately OVER-samples the edges — a stress harness earns its keep on the tails,
/// not the happy path (tracker §4.6 punkt 2). Each stratum is a distinct way the real
/// engines can be pushed:
/// <list type="bullet">
/// <item><see cref="CleanExactTitle"/> / <see cref="InflectedTitle"/> — the only strata
/// with a derivation <i>facit</i> (built from injected ground-truth pairs).</item>
/// <item>the rest are negative / adversarial / degraded inputs where only crash-safety
/// gates and the verdict distribution is observed (CTO Fork 5/6).</item>
/// </list>
/// A stratum applies to the title corpus, the CV corpus, or both — see
/// <see cref="CorpusStrata"/>.
/// </summary>
public enum CorpusStratum
{
    /// <summary>Title equals a real occupation-name label → expect an ExactOccupationName
    /// candidate resolving to the facit ssyk-4 group. Title + CV.</summary>
    CleanExactTitle,

    /// <summary>A deterministically inflected/extended variant of a real occupation-name
    /// → expect a non-empty (stemmed-overlap) candidate list. Title + CV.</summary>
    InflectedTitle,

    /// <summary>Empty, whitespace, or a single weak token → expect no match, never a
    /// crash. Title + CV.</summary>
    EmptyOrWeakSignal,

    /// <summary>A life-situation phrase ("Föräldraledig", "Sjukskriven", "Arbetssökande")
    /// — NOT an occupation → expected to never resolve to an SSYK group (observed as a
    /// headline finding if it ever does). Title + CV.</summary>
    LifeSituationGap,

    /// <summary>Non-standard or English titles ("Ninja Developer", "Software Engineer")
    /// → Swedish-stemming degrades them gracefully; any outcome, no crash. Title + CV.</summary>
    NonStandardOrEnglishTitle,

    /// <summary>Several occupational tracks stitched into one title/CV
    /// ("Snickare och systemutvecklare") → the engine PROPOSES, never selects (ADR 0040
    /// Beslut 4); any outcome, no crash. Title + CV.</summary>
    MultiTrack,

    /// <summary>OCR/encoding noise: mojibake, doubled spacing, stray separators
    /// → no crash. Title + CV.</summary>
    NoiseOrOcr,

    /// <summary>Newly-arrived profile: non-Swedish name, sparse content, mixed language
    /// → no crash; degraded-parse first-class (OQ5). CV (and a title variant).</summary>
    NewlyArrived,

    /// <summary>Adversarial input: very long text, control/null characters,
    /// injection-shaped tokens → crash-safety MUST hold. Title + CV.</summary>
    Adversarial,

    /// <summary>A Luhn-valid fake personnummer embedded in free text → the review engine's
    /// B4 criterion must surface it (observed) and the personnummer guard's PII-safe outcome
    /// rides on the aggregate. The raw value is NEVER echoed to a label, report, or log
    /// (ADR 0074 Invariant 1). CV only.</summary>
    FakePersonnummer,
}

/// <summary>Which strata feed the title corpus (deriver) vs the CV corpus (reviewer).</summary>
public static class CorpusStrata
{
    /// <summary>Strata that produce a free-text occupational <i>title</i> for the deriver.</summary>
    public static readonly IReadOnlyList<CorpusStratum> Title =
    [
        CorpusStratum.CleanExactTitle,
        CorpusStratum.InflectedTitle,
        CorpusStratum.EmptyOrWeakSignal,
        CorpusStratum.LifeSituationGap,
        CorpusStratum.NonStandardOrEnglishTitle,
        CorpusStratum.MultiTrack,
        CorpusStratum.NoiseOrOcr,
        CorpusStratum.NewlyArrived,
        CorpusStratum.Adversarial,
    ];

    /// <summary>Strata that produce a parsed CV (<c>ParsedResume</c>) for the reviewer.</summary>
    public static readonly IReadOnlyList<CorpusStratum> Cv =
    [
        CorpusStratum.CleanExactTitle,
        CorpusStratum.InflectedTitle,
        CorpusStratum.EmptyOrWeakSignal,
        CorpusStratum.LifeSituationGap,
        CorpusStratum.NonStandardOrEnglishTitle,
        CorpusStratum.MultiTrack,
        CorpusStratum.NoiseOrOcr,
        CorpusStratum.NewlyArrived,
        CorpusStratum.Adversarial,
        CorpusStratum.FakePersonnummer,
    ];
}
