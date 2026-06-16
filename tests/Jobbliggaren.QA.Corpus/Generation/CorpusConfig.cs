namespace Jobbliggaren.QA.Corpus.Generation;

/// <summary>
/// Reproducible corpus configuration: a fixed <see cref="Seed"/> plus a per-stratum quota
/// table that OVER-samples the edges (tracker §4.6 punkt 2). <see cref="Scale"/> multiplies
/// every quota so the corpus grows from the ~300 default to 770+ via a single config knob
/// without re-generating or shifting any existing row (CTO Fork 9, follows from 2B).
/// </summary>
public sealed record CorpusConfig
{
    /// <summary>Fixed seed → identical corpus across runs.</summary>
    public int Seed { get; init; } = 0x5C0_C1A2; // "STEG C" — an arbitrary fixed constant

    /// <summary>Per-stratum base counts at scale 1. Edges out-number the clean strata.</summary>
    public IReadOnlyDictionary<CorpusStratum, int> BaseCounts { get; init; } = DefaultBaseCounts;

    /// <summary>Multiplier on every base count (1 ≈ 300 cases across both corpora).</summary>
    public int Scale { get; init; } = 1;

    /// <summary>The resolved count for a stratum at the configured scale (≥ 0).</summary>
    public int CountFor(CorpusStratum stratum) =>
        BaseCounts.TryGetValue(stratum, out var n) ? n * Scale : 0;

    // Base quotas. The two facit-bearing strata (Clean/Inflected) are sized so the deriver
    // gets real title-breadth; the negative/adversarial strata are over-sampled because a
    // stress harness earns its value on the tails. Declared BEFORE Default so this static
    // field is initialised before Default's instance initialiser reads it (static init order).
    private static readonly IReadOnlyDictionary<CorpusStratum, int> DefaultBaseCounts =
        new Dictionary<CorpusStratum, int>
        {
            [CorpusStratum.CleanExactTitle] = 40,
            [CorpusStratum.InflectedTitle] = 30,
            [CorpusStratum.EmptyOrWeakSignal] = 20,
            [CorpusStratum.LifeSituationGap] = 25,
            [CorpusStratum.NonStandardOrEnglishTitle] = 30,
            [CorpusStratum.MultiTrack] = 25,
            [CorpusStratum.NoiseOrOcr] = 30,
            [CorpusStratum.NewlyArrived] = 20,
            [CorpusStratum.Adversarial] = 30,
            [CorpusStratum.FakePersonnummer] = 20,
        };

    /// <summary>The default ~300-case configuration (seed fixed, scale 1).</summary>
    public static CorpusConfig Default { get; } = new();
}
