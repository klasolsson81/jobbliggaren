namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// The provenance of a <see cref="ProposedChange"/>'s replacement text — the no-synthesis
/// enforcement token (Fas 4 STEG 10, F4-10; ADR 0074; BUILD §8.3: determinism DIAGNOSES and
/// STRUCTURES, never SYNTHESIZES). A CLOSED three-arm discriminated form (parity
/// <c>CitedEvidence</c>): the ONLY three legal origins of an <c>After</c> string are (1) a
/// verbatim knowledge-bank value, (2) a pure total transform of <c>Before</c>, or (3) a
/// mechanical substitution of user-selected, mechanically-verified inputs into a versioned
/// frame template (Fas 4b PR-7, ADR 0093 §D2's DELIBERATE widening of the original two-arm
/// contract — recorded there precisely because the union is documented as closed). There is
/// STILL no free-text arm — synthesised prose is unrepresentable by shape (CLAUDE.md §5).
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

/// <summary>
/// The <c>After</c> text is a deterministic sentence/measure frame's template with the
/// user's own inputs substituted (Fas 4b PR-7, ADR 0093 §D2 third arm; handoff §6.2 —
/// "samma indata + samma verb = samma utdata, alltid"). <paramref name="FrameId"/> names
/// the <c>frames.v1.json</c> frame; <paramref name="Verb"/> is the resolved lead verb (the
/// frame's fixed verb, or the user's verb-slot echo — always a member of the strong-verb
/// list at the catalog's pinned verb-mapping version, invariant b); <paramref name="UserInputs"/>
/// are the raw slot inputs the After was built from (noun slots token-grounded in the cited
/// Before span, invariant a; number slots verbatim user echo, invariant c — "aldrig
/// påhittade siffror"). Only <see cref="ProposedChange.FromFrame"/> can mint this arm, and
/// it BUILDS the After itself — a pre-built After is unrepresentable (CLAUDE.md §5).
/// </summary>
public sealed record UserParameterizedFrameProvenance(
    string FrameId, string Verb, IReadOnlyDictionary<string, string> UserInputs)
    : ChangeProvenance;
