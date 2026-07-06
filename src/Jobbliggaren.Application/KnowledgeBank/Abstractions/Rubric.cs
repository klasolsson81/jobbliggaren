namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// The five rubric categories (research §2.2–2.6). English members (CLAUDE.md §1);
/// the Swedish data tokens (<c>Innehåll/Struktur/Språk/ATS-parsbarhet/Visuell
/// kvalitet</c>) are mapped to these in the Infrastructure loader (senior-cto-advisor
/// DQ7 — explicit table, fail-loud on unknown).
/// </summary>
public enum RubricCategory
{
    Content,
    Structure,
    Language,
    AtsParsability,
    VisualQuality,
}

/// <summary>
/// Criterion importance tier (research §2.7). English members; Swedish data tokens
/// (<c>Kritisk/Hög/Medel/Låg</c>) mapped in the loader. The numeric multipliers
/// (3/2/1/0.5) are NOT on this enum — they live as data in <see cref="Rubric.Weights"/>,
/// keyed by this enum (no hardcoded threshold in C#, CLAUDE.md §5).
/// </summary>
public enum CriterionWeight
{
    Critical,
    High,
    Medium,
    Low,
}

/// <summary>
/// Which rendering profile a criterion applies to (research §2.2–2.6). A=B=C are
/// <see cref="Both"/>; D (ATS-parsbarhet) is <see cref="AtsOnly"/>; E (Visuell
/// kvalitet) is <see cref="VisualOnly"/>. Swedish data tokens (<c>Båda/EndastAts/
/// EndastVisuell</c>) mapped in the loader.
/// </summary>
public enum RubricProfile
{
    Both,
    AtsOnly,
    VisualOnly,
}

/// <summary>
/// Whether a criterion can be assessed by the deterministic engine, and at what tier
/// (ADR 0071's ~70/26/4 split). <see cref="NotAssessedV1"/> is the honesty contract
/// (CLAUDE.md §5: reduced-precision criteria are marked "not assessed v1", never
/// mis-reported) — A5 (career progression) and C1 (genuine grammar) are pinned here
/// per ADR 0071 OQ3. A MISSING <c>assessability</c> token in the data defaults to
/// <see cref="NotAssessedV1"/> in the loader (never over-reports assessability).
/// </summary>
public enum CriterionAssessability
{
    Deterministic,
    DeterministicPlusNlp,
    NotAssessedV1,
}

/// <summary>
/// The score-band label a percentage falls into (research §2.7). English members;
/// Swedish data tokens (<c>EjRedo/BehöverOmarbetning/Konkurrenskraftigt/Toppskikt</c>)
/// mapped in the loader. The Swedish UI copy belongs to <c>messages/sv.json</c>
/// (CLAUDE.md §10), not this asset — the asset carries the machine label only.
/// </summary>
public enum ScoreBandLabel
{
    NotReady,
    NeedsRework,
    Competitive,
    TopTier,
}

/// <summary>
/// One score band: a verdict produced by F4-9 whose category-weighted percentage is
/// <c>&gt;= <see cref="MinInclusive"/></c> (and below the next band's bound) maps to
/// <see cref="Label"/>. Carried as DATA in the rubric asset (research §2.7) — never a
/// hardcoded threshold in C# (CLAUDE.md §5).
/// </summary>
public sealed record ScoreBand(int MinInclusive, ScoreBandLabel Label);

/// <summary>
/// Per-profile category-blend weights (research §2.7): ATS total =
/// A·0.50 + B·0.20 + C·0.15 + D·0.15; Visual total = A·0.50 + B·0.15 + C·0.15 + E·0.20.
/// Carried as DATA (the scoring algorithm in F4-9 reads these — it never inlines them).
/// </summary>
public sealed record CategoryWeights(
    IReadOnlyDictionary<RubricCategory, double> Ats,
    IReadOnlyDictionary<RubricCategory, double> Visual);

/// <summary>
/// One rubric criterion (research §2.2–2.6). The static specification F4-9 assesses a
/// CV against — it deliberately carries NO numeric score (Goodhart parity with
/// <c>MatchDimension</c>, CLAUDE.md §5): the binary PASS/FAIL with cited evidence is
/// produced at assessment time, not baked into the rubric. <see cref="Weight"/> is the
/// tier ENUM (the multiplier lives in <see cref="Rubric.Weights"/> keyed by it).
/// <para>
/// Signal nullability follows <see cref="Profile"/> (loader-validated): an
/// <see cref="RubricProfile.AtsOnly"/> criterion (D1–D10) has null visual signals; a
/// <see cref="RubricProfile.VisualOnly"/> criterion (E1–E8) has null ATS signals; a
/// <see cref="RubricProfile.Both"/> criterion (A/B/C) has all four. Signal text is
/// human-readable Swedish copy verbatim from research §2.2–2.6, not a machine token.
/// </para>
/// <para>
/// <b>Threshold posture (rubric v1.2, Fas 4b PR-5 CTO-bind D1 — retires the v1 M1=(a)
/// posture of 2026-06-15).</b> Per-criterion numeric thresholds are versioned DATA in
/// <see cref="Thresholds"/> (named keys, see <see cref="RubricThresholdKeys"/>); the rules
/// read them via the fail-loud <see cref="RequiredThreshold"/> accessor — never a hardcoded
/// C# literal, never a silent fallback. The prose signals REMAIN the user-facing civic
/// explanation (SoC: explanation vs computation); golden drift-guards assert prose↔data
/// agreement where the prose carries the number. Detection-shape constants (regex bounds,
/// structural factors) stay code per ADR 0093 §D3 "algorithms are code".
/// </para>
/// </summary>
public sealed record RubricCriterion(
    string Id,
    RubricCategory Category,
    string Name,
    CriterionWeight Weight,
    RubricProfile Profile,
    CriterionAssessability Assessability,
    string? AtsPassSignal,
    string? AtsFailSignal,
    string? VisualPassSignal,
    string? VisualFailSignal,
    // Versioned, civic-Swedish user-facing reason a criterion reports when it produces a
    // NotAssessed verdict (ADR 0071: reasons are knowledge-bank DATA, never an inline C#
    // literal — the engine reads this instead of hardcoding the copy; CLAUDE.md §10). Null
    // for criteria that are always assessed, and tolerated null on an N-1 asset (the engine
    // falls back to a generic civic default). Carries no dev-jargon (POS/NER, ADR refs, "v1").
    string? NotAssessedReason = null,
    // Per-criterion named numeric thresholds (rubric v1.2, CTO-bind D1 Variant A) — a
    // keyed dict, NOT a scalar (the Goodhart shape-guard rejects a bare numeric prop; a
    // named tuning dict is explainable, not a hidden grade). Null on an N-1 asset — the
    // loader tolerates it; the LIVE engine requires the shipped asset to carry its keys
    // (RequiredThreshold fails loud; completeness-tested).
    IReadOnlyDictionary<string, double>? Thresholds = null,
    // "Ignorera regeln endast för stilfrågor" (handoff §5.3, CTO-bind D2): true marks a
    // cosmetic style criterion the user may Ignore; default FALSE = fail-closed (a
    // criterion missing the flag can never be silenced). The loader fails loud if a
    // styleOnly criterion appears in criticalFailIds.
    bool StyleOnly = false)
{
    /// <summary>
    /// The named threshold this criterion's rule requires (CTO-bind D1): fail-loud on a
    /// missing key — an asset↔rule drift must fail the review, never fall back to a
    /// resurrected C# literal. Keys come from <see cref="RubricThresholdKeys"/> constants,
    /// never inline strings (CLAUDE.md §5).
    /// </summary>
    public double RequiredThreshold(string key) =>
        Thresholds is not null && Thresholds.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Rubrikkriteriet {Id} saknar den obligatoriska tröskeln '{key}' — " +
                "rubric-asseten och regelkoden har driftat (fail-loud, ingen literal-fallback).");
}

/// <summary>
/// The named per-criterion threshold keys of <see cref="RubricCriterion.Thresholds"/>
/// (rubric v1.2, CTO-bind D1) — constants so no rule carries an inline magic string
/// (CLAUDE.md §5). Keys are shared where the semantics coincide (e.g. A2/A6 pass+fail
/// ratios) and criterion-specific where the honest unit differs (months, words, counts).
/// </summary>
public static class RubricThresholdKeys
{
    /// <summary>Min share for PASS (A2 strong-opener ratio; A6 concretion ratio).</summary>
    public const string PassRatio = "passRatio";

    /// <summary>Ratio bound for FAIL (A1 missing-metric &gt;; A2/A6 &lt;; C3 passive &gt;).</summary>
    public const string FailRatio = "failRatio";

    /// <summary>A4: employment-gap months above which a gap is reported (&gt;).</summary>
    public const string MaxGapMonths = "maxGapMonths";

    /// <summary>A7: hit count BELOW which the verdict is PASS (&lt;).</summary>
    public const string PassBelowCount = "passBelowCount";

    /// <summary>A7/A9: hit count FROM which the verdict is FAIL (&gt;=).</summary>
    public const string FailFromCount = "failFromCount";

    /// <summary>A8: profile word count above which the verdict is FAIL (&gt;).</summary>
    public const string MaxWords = "maxWords";

    /// <summary>C2: exclamation-mark count FROM which the verdict is WARN (&gt;=).</summary>
    public const string WarnFromExclamationCount = "warnFromExclamationCount";

    /// <summary>C6: max unexplained acronyms before WARN (&gt;).</summary>
    public const string MaxUnexplainedAcronyms = "maxUnexplainedAcronyms";

    /// <summary>B6: max distinct date formats before WARN (&gt;).</summary>
    public const string MaxDistinctDateFormats = "maxDistinctDateFormats";

    /// <summary>C7: suspected-misspelling count FROM which the verdict is WARN (&gt;=).</summary>
    public const string WarnFromMisspellingCount = "warnFromMisspellingCount";
}

/// <summary>
/// The versioned CV-quality rubric (F4-7, BUILD §8.1/§8.6, research §2). The whole
/// rubric is VERSIONED DATA loaded from an embedded asset — every threshold
/// (<see cref="Weights"/>, <see cref="CategoryWeights"/>, <see cref="Bands"/>) and the
/// criterion set is carried as data, never a hardcoded C# literal (CLAUDE.md §5).
/// Consumed by F4-9 (review) and F4-10 (build/improve); F4-7 ships the data + loader +
/// this contract only, not the scoring algorithm.
/// </summary>
public sealed record Rubric(
    RubricVersion Version,
    DateOnly EffectiveDate,
    IReadOnlyDictionary<CriterionWeight, double> Weights,
    CategoryWeights CategoryWeights,
    IReadOnlyList<ScoreBand> Bands,
    IReadOnlyList<string> CriticalFailIds,
    IReadOnlyList<RubricCriterion> Criteria);
