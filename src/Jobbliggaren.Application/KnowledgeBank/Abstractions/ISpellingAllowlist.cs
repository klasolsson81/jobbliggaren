namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Provides the committed, versioned spelling allowlist (Fas 4b PR-6, ADR 0093 §D4) —
/// proper nouns + technical terms a Swedish/English CV legitimately uses that the Hunspell
/// dictionaries do not carry, so the C7 spelling criterion never reports them as
/// misspellings. One more ISP port over the knowledge bank (parity
/// <see cref="IClicheLexicon"/>/<see cref="IVerbMapper"/>) — the allowlist is versioned KB
/// DATA, NEVER an inline C# list (CLAUDE.md §5). Infrastructure loads the embedded asset
/// once and caches the mapped contract (singleton).
/// </summary>
public interface ISpellingAllowlist
{
    /// <summary>The current committed spelling allowlist (<c>spelling-allowlist.v1.json</c>).</summary>
    SpellingAllowlist GetAllowlist();
}
