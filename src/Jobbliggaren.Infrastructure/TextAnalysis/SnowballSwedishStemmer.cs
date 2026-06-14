using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Snowball;

namespace Jobbliggaren.Infrastructure.TextAnalysis;

/// <summary>
/// <see cref="IStemmer"/> for Swedish via the Snowball algorithm
/// (libstemmer.net 2.2.3). Fas 4 STEG 2 (F4-2, ADR 0074). The produced stem MUST
/// match PostgreSQL <c>to_tsvector('swedish')</c>, because the product's
/// full-text search stores its <c>search_vector</c> with that stemmer and the
/// matching engine (F4-4/5/6) aligns query terms with the stored lexemes.
///
/// <para>
/// <b>Thread-safety (CTO Variant A).</b> The underlying
/// <see cref="SwedishStemmer"/> is STATEFUL (mutable internal buffer across a
/// <c>Stem</c> call) → not safe for concurrent calls on one instance, but a
/// single instance is reusable sequentially. This singleton therefore holds one
/// instance per thread via <c>[ThreadStatic]</c>: zero lock contention, amortised
/// zero allocation, and — unlike <see cref="System.Threading.ThreadLocal{T}"/> —
/// no <see cref="System.IDisposable"/> field on a process-lifetime singleton
/// (CA1001; the same instinct that kept <c>TaxonomyReadModel</c> off
/// <c>SemaphoreSlim</c>). A per-thread instance is never shared across an
/// <c>await</c>, so the sequential-use invariant holds.
/// </para>
/// </summary>
internal sealed class SnowballSwedishStemmer : IStemmer
{
    [ThreadStatic]
    private static SwedishStemmer? _swedishStemmer;

    public string Stem(string word, TextLanguage language)
    {
        ArgumentNullException.ThrowIfNull(word);

        if (language is not TextLanguage.Swedish)
        {
            throw new NotSupportedException(
                $"F4-2 implements Swedish stemming only ('not assessed v1', ADR 0074); " +
                $"'{language}' is wired at F4-8/9.");
        }

        // The Snowball Swedish algorithm operates on a lowercased token; the
        // analyzer owns lowercasing (mirroring how to_tsvector lowercases before
        // stemming). Reuse the per-thread instance — sequential calls are safe.
        var stemmer = _swedishStemmer ??= new SwedishStemmer();
        return stemmer.Stem(word);
    }
}
