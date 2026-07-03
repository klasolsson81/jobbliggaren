namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// The provenance of a <see cref="ProposedChange"/>'s replacement text — the no-synthesis
/// enforcement token (Fas 4 STEG 10, F4-10; ADR 0074; BUILD §8.3: determinism DIAGNOSES and
/// STRUCTURES, never SYNTHESIZES). A CLOSED two-arm discriminated form (parity
/// <c>CitedEvidence</c>): the ONLY two legal origins of an <c>After</c> string are (1) a
/// verbatim knowledge-bank value, or (2) a pure total transform of <c>Before</c>. There is
/// NO free-text arm — synthesised prose is unrepresentable by shape (CLAUDE.md §5).
/// </summary>
public abstract record ChangeProvenance;

/// <summary>
/// The <c>After</c> text is verbatim from the versioned knowledge bank: the
/// <c>DropInReplacement</c> (cliché) or <c>SuggestedStrong</c> (verb) resolved for
/// <paramref name="Key"/> in asset <paramref name="Source"/>@<paramref name="Version"/>.
/// <paramref name="Source"/> ∈ {"cliche-list", "verb-mapping"} (the only KB assets that carry
/// replacement strings).
/// </summary>
public sealed record KnowledgeBankProvenance(string Source, string Version, string Key)
    : ChangeProvenance;

/// <summary>
/// The <c>After</c> text (if any) is a pure total function of <c>Before</c>, produced by
/// re-running <paramref name="Transform"/>. Pure removals carry no <c>After</c>
/// (<c>Replacement</c> is null).
/// </summary>
public sealed record StructuralTransformProvenance(StructuralTransformKind Transform)
    : ChangeProvenance;
