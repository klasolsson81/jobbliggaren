using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Snowball;

namespace Jobbliggaren.Infrastructure.TextAnalysis;

/// <summary>
/// <see cref="IStemmer"/> via the Snowball algorithm (libstemmer.net 2.2.3),
/// language-dispatching. Fas 4 STEG 2 (F4-2, Swedish) + STEG 9 (F4-9, English
/// wired per the ADR 0074 F4-2 amendment — Shape A, a language parameter per call,
/// not a resolver/factory layer). The produced stem MUST match PostgreSQL
/// <c>to_tsvector(&lt;lang&gt;)</c>: the product's full-text search stores its
/// <c>search_vector</c> with that stemmer (Swedish corpus) and English CVs must
/// lexemize identically to <c>to_tsvector('english')</c> (the F4-9 parity gate).
///
/// <para>
/// <b>Thread-safety (CTO Variant A).</b> Each Snowball stemmer is STATEFUL (mutable
/// internal buffer across a <c>Stem</c> call) → not safe for concurrent calls on one
/// instance, but a single instance is reusable sequentially. This singleton therefore
/// holds one instance PER LANGUAGE PER THREAD via <c>[ThreadStatic]</c>: zero lock
/// contention, amortised zero allocation, and — unlike
/// <see cref="System.Threading.ThreadLocal{T}"/> — no <see cref="System.IDisposable"/>
/// field on a process-lifetime singleton (CA1001). A per-thread instance is never
/// shared across an <c>await</c>, so the sequential-use invariant holds.
/// </para>
/// </summary>
internal sealed class SnowballStemmer : IStemmer
{
    [ThreadStatic]
    private static SwedishStemmer? _swedishStemmer;

    [ThreadStatic]
    private static EnglishStemmer? _englishStemmer;

    public string Stem(string word, TextLanguage language)
    {
        ArgumentNullException.ThrowIfNull(word);

        // The Snowball algorithm operates on a lowercased token; the analyzer owns
        // lowercasing (mirroring how to_tsvector lowercases before stemming). Reuse the
        // per-thread instance — sequential calls are safe.
        return language switch
        {
            TextLanguage.Swedish => (_swedishStemmer ??= new SwedishStemmer()).Stem(word),
            TextLanguage.English => (_englishStemmer ??= new EnglishStemmer()).Stem(word),
            _ => throw new NotSupportedException(
                $"The local NLP tier stems Swedish and English only (ADR 0074); '{language}' is unsupported."),
        };
    }
}
