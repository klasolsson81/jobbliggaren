using System.Collections.Frozen;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;

namespace Jobbliggaren.Infrastructure.TextAnalysis;

/// <summary>
/// <see cref="ITextAnalyzer"/> reproducing the lexeme stream of PostgreSQL
/// <c>to_tsvector(&lt;lang&gt;)</c>: lowercase → tokenise (word tokens) → drop
/// stopwords → stem (Snowball). Fas 4 STEG 2 (F4-2, Swedish) + STEG 9 (F4-9,
/// English). Composes an <see cref="IStemmer"/> and loads the embedded
/// <c>swedish.stop</c> + <c>english.stop</c> (each byte-identical to PostgreSQL
/// 18.3's built-in list) once into <see cref="FrozenSet{T}"/>s. PostgreSQL's
/// text-search parser handling of URLs/e-mails/numbers/hyphenation is out of scope —
/// only word-token parity is guaranteed (CLAUDE.md §5, "not assessed v1").
/// </summary>
internal sealed class LocalTextAnalyzer : ITextAnalyzer
{
    private const string SwedishStopwordResourceName =
        "Jobbliggaren.Infrastructure.TextAnalysis.swedish.stop";
    private const string EnglishStopwordResourceName =
        "Jobbliggaren.Infrastructure.TextAnalysis.english.stop";

    // Reference data: immutable, identical for every caller, loaded once. Eager
    // (trivial cost) — only the Hunspell dictionary (heavy IO) warrants lazy-init.
    private static readonly FrozenSet<string> SwedishStopwords = LoadStopwords(SwedishStopwordResourceName);
    private static readonly FrozenSet<string> EnglishStopwords = LoadStopwords(EnglishStopwordResourceName);

    private readonly IStemmer _stemmer;

    public LocalTextAnalyzer(IStemmer stemmer) => _stemmer = stemmer;

    public IReadOnlyList<string> ToLexemes(string text, TextLanguage language)
    {
        ArgumentNullException.ThrowIfNull(text);

        var stopwords = language switch
        {
            TextLanguage.Swedish => SwedishStopwords,
            TextLanguage.English => EnglishStopwords,
            _ => throw new NotSupportedException(
                $"The local NLP tier analyses Swedish and English only (ADR 0074); '{language}' is unsupported."),
        };

        var lexemes = new List<string>();
        foreach (var token in Tokenize(text))
        {
            // Lowercase before the stopword check and stemming, exactly as
            // to_tsvector does (the embedded stopword lists are lowercase).
            var lowered = token.ToLowerInvariant();
            if (stopwords.Contains(lowered))
            {
                continue;
            }

            var stem = _stemmer.Stem(lowered, language);
            if (!string.IsNullOrEmpty(stem))
            {
                lexemes.Add(stem);
            }
        }

        return lexemes;
    }

    // Word-token split: maximal runs of letters/digits; everything else
    // (whitespace, punctuation, hyphens) is a separator. åäö are letters so they
    // stay inside tokens. Word-token parity with to_tsvector only.
    private static IEnumerable<string> Tokenize(string text)
    {
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }

        if (start >= 0)
        {
            yield return text[start..];
        }
    }

    private static FrozenSet<string> LoadStopwords(string resourceName)
    {
        var assembly = typeof(LocalTextAnalyzer).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded stopword list missing: {resourceName}. " +
                "Verify <EmbeddedResource> in Jobbliggaren.Infrastructure.csproj.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var words = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                words.Add(trimmed);
            }
        }

        // Ordinal: tokens are already ToLowerInvariant-ed and the lists are
        // lowercase UTF-8, so exact ordinal match covers åäö without culture cost.
        return words.ToFrozenSet(StringComparer.Ordinal);
    }
}
