namespace Jobbliggaren.Application.Common.Abstractions.TextAnalysis;

/// <summary>
/// Deterministic spell-checking against a language-specific Hunspell dictionary
/// (Swedish = sv_SE DSSO, LGPL-3.0, shipped as a separate unmodified data file —
/// BUILD §3.1 copyleft separation). The first real consumer is the CV review
/// engine (F4-9/10); the port is defined in F4-2 per the Full-tier decision but
/// has no F4-2 consumer (guarded by an empty consumer-allowlist arch-test so no
/// premature coupling creeps in). <see cref="Suggest"/> returns candidates only —
/// a rule engine never silently rewrites the user's text (CLAUDE.md §5).
/// F4-2 implements <see cref="TextLanguage.Swedish"/> only.
/// </summary>
public interface ISpellChecker
{
    /// <summary>
    /// True if <paramref name="word"/> is spelled correctly for
    /// <paramref name="language"/>. F4-2 supports <see cref="TextLanguage.Swedish"/>
    /// only; other languages throw <see cref="System.NotSupportedException"/>.
    /// </summary>
    bool Check(string word, TextLanguage language);

    /// <summary>
    /// Ordered spelling suggestions for <paramref name="word"/> in
    /// <paramref name="language"/> (empty when the word is correctly spelled or no
    /// candidate is found). Candidates only — never an applied correction.
    /// </summary>
    IReadOnlyList<string> Suggest(string word, TextLanguage language);
}
