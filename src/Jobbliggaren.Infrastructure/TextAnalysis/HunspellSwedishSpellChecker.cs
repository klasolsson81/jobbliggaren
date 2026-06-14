using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using WeCantSpell.Hunspell;

namespace Jobbliggaren.Infrastructure.TextAnalysis;

/// <summary>
/// <see cref="ISpellChecker"/> for Swedish via Hunspell (WeCantSpell.Hunspell
/// 7.0.1) against the sv_SE DSSO dictionary. Fas 4 STEG 2 (F4-2, ADR 0074).
/// The dictionary ships as a separate, unmodified data file (Content, not an
/// embedded resource — BUILD §3.1 LGPL-3.0 copyleft separation) and is located at
/// runtime relative to <see cref="AppContext.BaseDirectory"/>.
///
/// <para>
/// <b>Lazy load.</b> The <see cref="WordList"/> (~2.3 MB / 150k words) loads on
/// first <see cref="Check"/>/<see cref="Suggest"/>, not at boot (architect/CTO
/// review). A loaded <see cref="WordList"/> is immutable, so it is shared from
/// this singleton. The one-time load is serialised by a plain monitor (not
/// <c>SemaphoreSlim</c> → no <see cref="IDisposable"/> field/CA1001, the same
/// instinct as <c>TaxonomyReadModel</c>); a failed load is NOT cached (the field
/// stays null and the next call retries), avoiding a permanent fault-cache.
/// </para>
///
/// <para>F4-2 has no consumer — the first is the CV review engine (F4-9/10).</para>
/// </summary>
internal sealed class HunspellSwedishSpellChecker : ISpellChecker
{
    /// <summary>Runtime path of the sv_SE DSSO dictionary file (Content, copied to output).</summary>
    internal static string DictionaryPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "TextAnalysis", "sv_SE.dic");

    /// <summary>Runtime path of the sv_SE DSSO affix file (Content, copied to output).</summary>
    internal static string AffixPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "TextAnalysis", "sv_SE.aff");

    private readonly object _loadGate = new();
    private volatile WordList? _wordList;

    public bool Check(string word, TextLanguage language)
    {
        ArgumentNullException.ThrowIfNull(word);
        EnsureSwedish(language);
        return GetWordList().Check(word);
    }

    public IReadOnlyList<string> Suggest(string word, TextLanguage language)
    {
        ArgumentNullException.ThrowIfNull(word);
        EnsureSwedish(language);

        // Materialise Hunspell's lazy IEnumerable<string> to IReadOnlyList so no
        // deferred enumeration leaks past the port (CTO review). Candidates only —
        // the caller never gets an applied correction (CLAUDE.md §5).
        return GetWordList().Suggest(word).ToArray();
    }

    private WordList GetWordList()
    {
        var loaded = _wordList;
        if (loaded is not null)
        {
            return loaded;
        }

        // Double-checked lock: serialise the one-time ~2.3 MB load (avoids a
        // concurrent double-load) without caching a failed load.
        lock (_loadGate)
        {
            return _wordList ??= WordList.CreateFromFiles(DictionaryPath, AffixPath);
        }
    }

    private static void EnsureSwedish(TextLanguage language)
    {
        if (language is not TextLanguage.Swedish)
        {
            throw new NotSupportedException(
                $"F4-2 implements Swedish spell-checking only ('not assessed v1', " +
                $"ADR 0074); '{language}' is wired at F4-8/9.");
        }
    }
}
