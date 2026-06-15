namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// One family of strong Swedish action verbs in preteritum (research §6.3), e.g.
/// "Ledarskap &amp; ansvar". The curated vocabulary the determinism endorses.
/// </summary>
public sealed record StrongVerbGroup(string Group, IReadOnlyList<string> Verbs);

/// <summary>
/// A weak verb/phrase to avoid (research §6.3 — e.g. "var ansvarig för") mapped to a
/// suggested strong replacement. <see cref="SuggestedStrong"/> MUST be one of the
/// verbs in some <see cref="StrongVerbGroup"/> (closure invariant — the propose step
/// never recommends an unendorsed verb). <see cref="Group"/> is an optional pointer to
/// the source group (cross-ref aid; null allowed).
/// </summary>
public sealed record WeakVerbMapping(string Weak, string SuggestedStrong, string? Group);

/// <summary>
/// The versioned weak→strong verb mapping (F4-7, research §6.3). The SINGLE
/// machine-readable source of the weak-verb list (senior-cto-advisor DQ8 — rubric
/// A2/C3 fail-signal prose may name example verbs freely, but the engine reads only
/// here). Versioned DATA (CLAUDE.md §5: "action-verb lists ... versioned data/config
/// ... not inline strings"). Plain-string version (DQ3).
/// </summary>
public sealed record VerbMapping(
    string Version,
    IReadOnlyList<StrongVerbGroup> StrongVerbGroups,
    IReadOnlyList<WeakVerbMapping> WeakVerbs);
