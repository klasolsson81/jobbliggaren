namespace Jobbliggaren.Application.Common.Abstractions.TextAnalysis;

/// <summary>
/// Normalises free text into the lexeme stream that PostgreSQL
/// <c>to_tsvector(&lt;config&gt;, text)</c> would produce for the same language:
/// lowercase, tokenise into word tokens, drop language stopwords, then stem each
/// remaining token (Snowball). This is the unit the matching engine (F4-4/5/6)
/// uses to align CV and job-ad terms with the stored <c>search_vector</c>.
/// PostgreSQL's text-search parser handling of URLs/e-mails/numbers is out of
/// scope — only word-token parity is guaranteed (CLAUDE.md §5: reduced-precision
/// token classes are "not assessed v1", never mis-reported). F4-2 implements
/// <see cref="TextLanguage.Swedish"/> only.
/// </summary>
public interface ITextAnalyzer
{
    /// <summary>
    /// Returns the ordered lexemes of <paramref name="text"/> for
    /// <paramref name="language"/> (lowercase → tokenise → stopword-filter → stem).
    /// Stopwords and empty tokens are dropped, matching <c>to_tsvector</c>.
    /// F4-2 supports <see cref="TextLanguage.Swedish"/> only; other languages throw
    /// <see cref="System.NotSupportedException"/>.
    /// </summary>
    IReadOnlyList<string> ToLexemes(string text, TextLanguage language);
}
