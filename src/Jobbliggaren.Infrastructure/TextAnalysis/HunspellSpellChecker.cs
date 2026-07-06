using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using WeCantSpell.Hunspell;

namespace Jobbliggaren.Infrastructure.TextAnalysis;

/// <summary>
/// <see cref="ISpellChecker"/> via Hunspell (WeCantSpell.Hunspell 7.0.1),
/// language-dispatching. Fas 4 STEG 2 (F4-2, sv_SE DSSO) + STEG 9 (F4-9, en_US).
/// Each dictionary ships as a separate, unmodified data file (Content, not an
/// embedded resource): sv_SE is LGPL-3.0 (BUILD §3.1 copyleft separation) and en_US
/// is permissive SCOWL/Ispell BSD — both located at runtime relative to
/// <see cref="AppContext.BaseDirectory"/>.
///
/// <para>
/// <b>Lazy load.</b> Each <see cref="WordList"/> loads on first
/// <see cref="Check"/>/<see cref="Suggest"/> for that language, not at boot
/// (architect/CTO review). A loaded <see cref="WordList"/> is immutable, so it is
/// shared from this singleton. The one-time load is serialised by a plain monitor
/// (not <c>SemaphoreSlim</c> → no <see cref="IDisposable"/> field/CA1001); a failed
/// load is NOT cached (the field stays null and the next call retries), avoiding a
/// permanent fault-cache.
/// </para>
///
/// <para>The first <see cref="ISpellChecker"/> consumer is the review engine's C7
/// machine-spelling criterion (Fas 4b PR-6) — a SEPARATE criterion that lights up BOTH
/// dictionaries (Swedish CVs still cite English tech terms via C7's allowlist). C1
/// (Stavning/grammatik) stays NotAssessedV1 (ADR 0071 OQ3) — Hunspell is not a grammar
/// checker, so C7 never takes the grammar half nor the critical slot.</para>
/// </summary>
internal sealed class HunspellSpellChecker : ISpellChecker
{
    /// <summary>Runtime path of the sv_SE DSSO dictionary file (Content, copied to output).</summary>
    internal static string DictionaryPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "TextAnalysis", "sv_SE.dic");

    /// <summary>Runtime path of the sv_SE DSSO affix file (Content, copied to output).</summary>
    internal static string AffixPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "TextAnalysis", "sv_SE.aff");

    /// <summary>Runtime path of the en_US dictionary file (Content, copied to output).</summary>
    internal static string EnglishDictionaryPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "TextAnalysis", "en_US.dic");

    /// <summary>Runtime path of the en_US affix file (Content, copied to output).</summary>
    internal static string EnglishAffixPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "TextAnalysis", "en_US.aff");

    private readonly object _swedishGate = new();
    private readonly object _englishGate = new();
    private volatile WordList? _swedishWordList;
    private volatile WordList? _englishWordList;

    public bool Check(string word, TextLanguage language)
    {
        ArgumentNullException.ThrowIfNull(word);
        return GetWordList(language).Check(word);
    }

    public IReadOnlyList<string> Suggest(string word, TextLanguage language)
    {
        ArgumentNullException.ThrowIfNull(word);

        // Materialise Hunspell's lazy IEnumerable<string> to IReadOnlyList so no
        // deferred enumeration leaks past the port (CTO review). Candidates only —
        // the caller never gets an applied correction (CLAUDE.md §5).
        return GetWordList(language).Suggest(word).ToArray();
    }

    private WordList GetWordList(TextLanguage language) => language switch
    {
        TextLanguage.Swedish => GetSwedishWordList(),
        TextLanguage.English => GetEnglishWordList(),
        _ => throw new NotSupportedException(
            $"The local NLP tier spell-checks Swedish and English only (ADR 0074); '{language}' is unsupported."),
    };

    private WordList GetSwedishWordList()
    {
        var loaded = _swedishWordList;
        if (loaded is not null)
        {
            return loaded;
        }

        // Double-checked lock: serialise the one-time load (avoids a concurrent
        // double-load) without caching a failed load.
        lock (_swedishGate)
        {
            return _swedishWordList ??= WordList.CreateFromFiles(DictionaryPath, AffixPath);
        }
    }

    private WordList GetEnglishWordList()
    {
        var loaded = _englishWordList;
        if (loaded is not null)
        {
            return loaded;
        }

        lock (_englishGate)
        {
            return _englishWordList ??= WordList.CreateFromFiles(EnglishDictionaryPath, EnglishAffixPath);
        }
    }
}
