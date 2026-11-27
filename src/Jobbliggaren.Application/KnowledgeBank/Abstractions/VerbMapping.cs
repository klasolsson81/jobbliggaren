namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// How a verb list holds its strong verbs, and so how a bullet's opener is matched against them.
/// The list declares it (ADR 0191 D7). Weak phrases are matched by their listed form in both modes.
/// </summary>
public enum StrongVerbMatch
{
    /// <summary>The list holds one form per verb, and the verb's stem stands for its inflections:
    /// an opener is strong when its first word has the stem of a listed verb (the Swedish list).
    /// </summary>
    Stem,

    /// <summary>The list holds every inflected form it endorses (base, -s, past, -ing) and no derived
    /// word: an opener is strong when it starts with a listed form on a word boundary (the English
    /// list). The English stemmer reads a derived noun as its verb ("development" as "develop").
    /// </summary>
    ListedForm,
}

/// <summary>
/// One family of strong action verbs, e.g. "Ledarskap &amp; ansvar". The curated vocabulary the
/// determinism endorses; what a verb string is depends on the list's <see cref="StrongVerbMatch"/>.
/// </summary>
public sealed record StrongVerbGroup(string Group, IReadOnlyList<string> Verbs);

/// <summary>
/// A weak verb/phrase to avoid (research §6.3 — e.g. "var ansvarig för") mapped to a
/// suggested strong replacement. <see cref="SuggestedStrong"/> MUST be one of the
/// verbs in some <see cref="StrongVerbGroup"/> (closure invariant — the propose step
/// never recommends an unendorsed verb). <see cref="Group"/> is an optional pointer to
/// the source group (cross-ref aid; null allowed).
/// <para>
/// <see cref="DropInSafe"/> (#494) gates whether F4-10's <c>WeakVerbTransform</c> may emit a
/// literal <c>ProposedReplacement</c>: only a pair with the SAME valency/rection (e.g.
/// "var ansvarig för" → "ansvarade för") is a grammatical drop-in. A non-drop-in pair — a
/// double finite verb ("var med och" → "genomförde") or a role-overreach ("deltog i" →
/// "genomförde", which ADR 0071 forbids inventing) — is still a FLAGGED weak opener via the
/// F4-9 A2 review verdict, but the improve engine proposes NO rewrite for it.
/// </para>
/// </summary>
public sealed record WeakVerbMapping(string Weak, string SuggestedStrong, string? Group, bool DropInSafe);

/// <summary>
/// The versioned weak→strong verb mapping (F4-7, research §6.3). The SINGLE
/// machine-readable source of the weak-verb list (senior-cto-advisor DQ8 — rubric
/// A2/C3 fail-signal prose may name example verbs freely, but the engine reads only
/// here). Versioned DATA (CLAUDE.md §5: "action-verb lists ... versioned data/config
/// ... not inline strings"). Plain-string version (DQ3).
/// </summary>
/// <param name="Source">The asset name a change proposed from this mapping cites as its
/// provenance (<c>KnowledgeBankProvenance.Source</c>).</param>
/// <param name="StrongVerbMatch">How the strong verbs are listed and matched.</param>
public sealed record VerbMapping(
    string Version,
    IReadOnlyList<StrongVerbGroup> StrongVerbGroups,
    IReadOnlyList<WeakVerbMapping> WeakVerbs,
    string Source,
    StrongVerbMatch StrongVerbMatch);
