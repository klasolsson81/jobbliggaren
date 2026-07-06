namespace Jobbliggaren.Application.Common.Abstractions.TextAnalysis;

/// <summary>
/// Deterministic spell-checking against a language-specific Hunspell dictionary
/// (Swedish = sv_SE DSSO, LGPL-3.0, shipped as a separate unmodified data file —
/// BUILD §3.1 copyleft separation). Defined in F4-2 per the Full-tier decision;
/// the first real consumer is the CV review engine's C7 machine-spelling criterion
/// (Fas 4b PR-6) — a SEPARATE criterion, not C1 (which stays NotAssessedV1 since
/// Hunspell is not a grammar checker). The consumer set is pinned by an arch-test
/// (<c>TextAnalysisLayerTests</c>) so no premature coupling creeps in.
/// <see cref="Suggest"/> returns candidates only — a rule engine never silently
/// rewrites the user's text (CLAUDE.md §5). Implements
/// <see cref="TextLanguage.Swedish"/> (F4-2) and <see cref="TextLanguage.English"/> (F4-9).
/// </summary>
public interface ISpellChecker
{
    /// <summary>
    /// True if <paramref name="word"/> is spelled correctly for
    /// <paramref name="language"/>. Supports <see cref="TextLanguage.Swedish"/> (F4-2)
    /// and <see cref="TextLanguage.English"/> (F4-9); other languages throw
    /// <see cref="System.NotSupportedException"/>.
    /// </summary>
    bool Check(string word, TextLanguage language);

    /// <summary>
    /// Ordered spelling suggestions for <paramref name="word"/> in
    /// <paramref name="language"/> (empty when the word is correctly spelled or no
    /// candidate is found). Candidates only — never an applied correction.
    /// </summary>
    IReadOnlyList<string> Suggest(string word, TextLanguage language);
}
