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
/// <b>v1 threshold posture (senior-cto-advisor M1=(a), 2026-06-15).</b> These prose signals
/// carry the per-criterion thresholds as text (e.g. A2 "≥80 %"). The F4-9 review engine
/// operationalises them as code (no machine-readable per-criterion threshold FIELD exists by
/// F4-7 design — DQ8). This is honest because the rubric is versioned (rubric@x.y.z; §2.8
/// minor = tröskel) and the version rides on every assessment. A structured per-criterion
/// threshold schema is a future rubric-vN / F4-10 forward-note (ADR 0074 discovery→STEG), NOT
/// a TD; the cliché/verb LISTS (§5) already ARE data.
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
    string? VisualFailSignal);

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
