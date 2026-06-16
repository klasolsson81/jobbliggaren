namespace Jobbliggaren.QA.Corpus.Generation;

/// <summary>
/// What the deriver is expected to do with a generated title. The harness GATES only on
/// crash-safety + the bearing invariant (CTO Fork 4/5); derivation accuracy is OBSERVED,
/// not gated (CTO Fork 6 — fitness functions stay observe-only until a Klas ratchet,
/// CLAUDE.md §2.5). So these expectations drive the findings report's per-stratum hit-rate,
/// they are not assertion oracles.
/// </summary>
public enum DerivationExpectation
{
    /// <summary>Should resolve to a specific ssyk-4 group (CleanExactTitle — the facit).</summary>
    ResolvesToFacitGroup,

    /// <summary>Should yield a non-empty candidate list (InflectedTitle).</summary>
    NonEmptyCandidates,

    /// <summary>A life-situation phrase — should NOT resolve to any SSYK group
    /// (LifeSituationGap; a violation is a headline finding).</summary>
    NeverResolvesToSsyk,

    /// <summary>No correctness expectation — only crash-safety matters (empty/noise/
    /// adversarial/multi-track/non-standard).</summary>
    AnyOutcome,
}

/// <summary>
/// A single deriver corpus case: the title fed to <c>IOccupationCodeDeriver.DeriveAsync</c>,
/// its stratum, and the (observe-only) expectation. <see cref="ExpectedSsyk4ConceptId"/> is
/// set only for <see cref="DerivationExpectation.ResolvesToFacitGroup"/>. <see cref="Label"/>
/// is a PII-safe identifier (stratum + index) — the <see cref="Title"/> itself never contains
/// PII (titles are occupational labels / synthetic noise; personnummer live only in CV cases).
/// </summary>
public sealed record GeneratedTitleCase(
    int Index,
    CorpusStratum Stratum,
    string Title,
    DerivationExpectation Expectation,
    string? ExpectedSsyk4ConceptId)
{
    /// <summary>Stable, PII-safe case label for reports and assertion messages.</summary>
    public string Label => $"{Stratum}#{Index}";
}
