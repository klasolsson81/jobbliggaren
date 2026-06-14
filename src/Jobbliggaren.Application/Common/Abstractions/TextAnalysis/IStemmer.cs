namespace Jobbliggaren.Application.Common.Abstractions.TextAnalysis;

/// <summary>
/// Reduces a single word to its language-specific stem (Snowball algorithm).
/// The stem MUST match what PostgreSQL <c>to_tsvector(&lt;config&gt;, word)</c>
/// produces for the same language, because the product's full-text search stores
/// its <c>search_vector</c> with that very stemmer — the matching engine
/// (F4-4/5/6) aligns query terms with those stored lexemes. Consistency against
/// <c>to_tsvector('swedish')</c> is the hard acceptance criterion for F4-2
/// (ADR 0074); non-trivial drift triggers a reactive STEG before F4-4, never a TD.
/// </summary>
public interface IStemmer
{
    /// <summary>
    /// Stems <paramref name="word"/> for <paramref name="language"/>. The input is
    /// expected to be a single, already-lowercased token — the analyzer owns
    /// lowercasing, mirroring how <c>to_tsvector</c> lowercases before stemming.
    /// F4-2 supports <see cref="TextLanguage.Swedish"/> only; other languages throw
    /// <see cref="System.NotSupportedException"/> (implementation path at F4-8/9).
    /// </summary>
    string Stem(string word, TextLanguage language);
}
