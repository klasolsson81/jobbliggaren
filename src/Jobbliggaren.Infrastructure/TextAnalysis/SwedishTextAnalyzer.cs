using System.Collections.Frozen;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;

namespace Jobbliggaren.Infrastructure.TextAnalysis;

/// <summary>
/// <see cref="ITextAnalyzer"/> for Swedish — reproduces the lexeme stream of
/// PostgreSQL <c>to_tsvector('swedish')</c>: lowercase → tokenise (word tokens)
/// → drop stopwords → stem (Snowball). Fas 4 STEG 2 (F4-2, ADR 0074).
/// Composes an <see cref="IStemmer"/> and loads the embedded <c>swedish.stop</c>
/// (byte-identical to PostgreSQL 18.3's built-in list) once into a
/// <see cref="FrozenSet{T}"/>. PostgreSQL's text-search parser handling of
/// URLs/e-mails/numbers/hyphenation is out of scope — only word-token parity is
/// guaranteed (CLAUDE.md §5, "not assessed v1").
/// </summary>
internal sealed class SwedishTextAnalyzer : ITextAnalyzer
{
    private const string StopwordResourceName =
        "Jobbliggaren.Infrastructure.TextAnalysis.swedish.stop";

    // Reference data: immutable, identical for every caller, loaded once. Eager
    // (trivial cost) — only the Hunspell dictionary (heavy IO) warrants lazy-init.
    private static readonly FrozenSet<string> SwedishStopwords = LoadStopwords();

    private readonly IStemmer _stemmer;

    public SwedishTextAnalyzer(IStemmer stemmer) => _stemmer = stemmer;

    public IReadOnlyList<string> ToLexemes(string text, TextLanguage language)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (language is not TextLanguage.Swedish)
        {
            throw new NotSupportedException(
                $"F4-2 implements Swedish text analysis only ('not assessed v1', " +
                $"ADR 0074); '{language}' is wired at F4-8/9.");
        }

        var lexemes = new List<string>();
        foreach (var token in Tokenize(text))
        {
            // Lowercase before the stopword check and stemming, exactly as
            // to_tsvector does (the embedded stopword list is lowercase).
            var lowered = token.ToLowerInvariant();
            if (SwedishStopwords.Contains(lowered))
            {
                continue;
            }

            var stem = _stemmer.Stem(lowered, TextLanguage.Swedish);
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

    private static FrozenSet<string> LoadStopwords()
    {
        var assembly = typeof(SwedishTextAnalyzer).Assembly;
        using var stream = assembly.GetManifestResourceStream(StopwordResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded stopword list missing: {StopwordResourceName}. " +
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

        // Ordinal: tokens are already ToLowerInvariant-ed and the list is
        // lowercase UTF-8, so exact ordinal match covers åäö without culture cost.
        return words.ToFrozenSet(StringComparer.Ordinal);
    }
}
