namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// The two deterministic frame mechanics (handoff §6.2; ADR 0093 §D2/§D3):
/// <see cref="Sentence"/> = a per-verb sentence frame (A2/C3 weak-opener rewrite),
/// <see cref="Measure"/> = the measure frame (A1 quantification, user-echoed number).
/// Field/format fixes (§6.2 mechanic 3) are pure string transforms — algorithm, not
/// frame data (ADR 0093 §D3 "algorithms are code").
/// </summary>
public enum FrameKind
{
    Sentence,
    Measure,
}

/// <summary>
/// The provenance class of one frame slot — each kind maps to one of the ADR 0093 §D2
/// <c>FromFrame</c> invariants the PR-7 apply-half enforces (PR-5 ships data + structural
/// validation only): <see cref="Noun"/> slots are filled from tokens present in the cited
/// Before-span (invariant a — never synthesised); a <see cref="Verb"/> slot is filled from
/// the knowledge-bank strong-verb list at the catalog's pinned version (invariant b — never
/// a free verb); a <see cref="Number"/> slot equals exactly the user's own echoed input
/// (invariant c — "aldrig påhittade siffror"); <see cref="Text"/> is a small
/// user-parameterized token (e.g. the measure frame's period word), the
/// UserParameterizedFrameProvenance arm (§D2 third provenance arm).
/// </summary>
public enum FrameSlotKind
{
    Noun,
    Verb,
    Number,
    Text,
}

/// <summary>One named placeholder in a frame template, e.g. <c>del1</c>/<c>kontext</c>.</summary>
public sealed record FrameSlot(string Name, FrameSlotKind Kind);

/// <summary>
/// One deterministic frame (handoff §6.2 "mönstret", generalised with named slots from
/// the prototype's Klas-example FRAMES): same input + same verb = same output, always.
/// <para>
/// <see cref="Verb"/> is the FIXED lead verb of a <see cref="FrameKind.Sentence"/> frame
/// (baked capitalised into <see cref="Template"/>) and MUST resolve in the strong-verb
/// groups of the verb mapping at <see cref="FrameCatalog.VerbMappingVersion"/> — the
/// loader fails loud otherwise. A <see cref="FrameKind.Measure"/> frame carries
/// <c>Verb == null</c> and instead a single <see cref="FrameSlotKind.Verb"/> slot the
/// user fills at apply time. <see cref="CriterionIds"/> names the rubric criteria whose
/// findings the frame remedies (A2/C3 sentence, A1 measure).
/// </para>
/// </summary>
public sealed record CvFrame(
    string Id,
    FrameKind Kind,
    IReadOnlyList<string> CriterionIds,
    string? Verb,
    IReadOnlyList<FrameSlot> Slots,
    string Template);

/// <summary>
/// The versioned catalog of deterministic sentence/measure frames the PR-7 apply
/// mechanism consumes (<c>frames.v1.json</c>, ADR 0093 §D3 — the hard sequencing
/// dependency built first). Versioned DATA (CLAUDE.md §5), plain-string version (DQ3
/// parity with the verb mapping). <see cref="VerbMappingVersion"/> pins the verb-mapping
/// version the frames were authored and validated against — the loader fails loud on a
/// mismatch, so a verb-list bump forces a deliberate frames re-validation (the §D2
/// "verb list at a specific version" contract made literal).
/// </summary>
public sealed record FrameCatalog(
    string Version,
    string VerbMappingVersion,
    IReadOnlyList<CvFrame> Frames);
