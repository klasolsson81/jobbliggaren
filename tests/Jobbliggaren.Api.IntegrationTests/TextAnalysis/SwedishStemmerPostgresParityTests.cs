using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.TextAnalysis;

// Fas 4 STEG 2 (F4-2) — THE HARD GATE (CTO-dom in-block 6+7, ADR 0074 hard
// acceptance criterion). Proves the local Snowball stemmer + analyzer pipeline
// is byte-identical to PostgreSQL 18.3 to_tsvector('swedish'). If this drifts,
// search_vector (JobAdSearchQuery — "Måste matcha EXAKT") diverges from the
// matching engine (F4-4/5/6). Drift triggers a reactive STEG, never a TD.
//
// Self-contained fixture (own PostgreSqlContainer, IAsyncLifetime) — mirrors
// TaxonomyReadModelIntegrationTests. No migrations needed: to_tsvector and
// pg_read_file are built-in / superuser-available in the Testcontainers image.
//
// Scope: WORD-token parity only. PG parser token-classes for URLs/e-mails/
// numbers/hyphenation are "not assessed v1" (CLAUDE.md §5) and out of scope —
// the corpus uses plain Swedish word tokens.
//
// RED until SnowballSwedishStemmer + SwedishTextAnalyzer ship.
public sealed class SwedishStemmerPostgresParityTests : IAsyncLifetime
{
    private const string PgStopwordPath =
        "/usr/share/postgresql/18/tsearch_data/swedish.stop";

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18").Build();

    private NpgsqlConnection _conn = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await _conn.OpenAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------
    // SUT factories — exact ctor signatures CC will create.
    // ---------------------------------------------------------------
    private static SnowballSwedishStemmer NewStemmer() => new();

    private static SwedishTextAnalyzer NewAnalyzer()
        => new(new SnowballSwedishStemmer());

    // ---------------------------------------------------------------
    // PG oracle helpers (parameterised — never string concatenation).
    // ---------------------------------------------------------------

    // Returns to_tsvector('swedish', word)::text, e.g. "'lär':1" or "" (empty).
    private async Task<string> ToTsvectorTextAsync(string word, CancellationToken ct)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT to_tsvector('swedish', @w)::text";
        cmd.Parameters.AddWithValue("w", word);
        return (string)(await cmd.ExecuteScalarAsync(ct))!;
    }

    // Parses the SINGLE lexeme out of "'lexeme':1" (single-word input).
    // Throws if the input did not produce exactly one lexeme (caller guards
    // against stopwords/multi-token inputs first).
    private static string ParseSingleLexeme(string tsvectorText)
    {
        var trimmed = tsvectorText.Trim();
        var firstQuote = trimmed.IndexOf('\'');
        var lastQuote = trimmed.LastIndexOf('\'');
        if (firstQuote < 0 || lastQuote <= firstQuote)
        {
            throw new InvalidOperationException(
                $"Förväntade en lexem i to_tsvector-output men fick: '{tsvectorText}'");
        }

        var lexeme = trimmed[(firstQuote + 1)..lastQuote];
        // A single-word input must yield a single lexeme — reject embedded
        // separators so a multi-lexeme parse can't slip through silently.
        lexeme.ShouldNotContain("':");
        return lexeme;
    }

    // Parses the distinct set of lexemes from a multi-word to_tsvector::text,
    // e.g. "'erfaren':1 'lär':2" → { "erfaren", "lär" }.
    private static HashSet<string> ParseDistinctLexemes(string tsvectorText)
    {
        var lexemes = new HashSet<string>(StringComparer.Ordinal);
        var span = tsvectorText.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            if (span[i] == '\'')
            {
                var end = span[(i + 1)..].IndexOf('\'');
                if (end < 0) break;
                lexemes.Add(span.Slice(i + 1, end).ToString());
                i += end + 2; // skip closing quote
            }
            else
            {
                i++;
            }
        }

        return lexemes;
    }

    // ===============================================================
    // 1. Stemmer ≡ PG for every non-stopword in the corpus
    // ===============================================================

    // Golden set + extra real job titles + common terms. NON-STOPWORDS only —
    // each must produce exactly one lexeme in to_tsvector('swedish').
    public static TheoryData<string> NonStopwordCorpus()
    {
        var words = new[]
        {
            // golden set
            "lärare", "läraren", "lärarens", "lärarna",
            "utvecklare", "utvecklaren", "utvecklarna",
            "system", "systemet", "systemen",
            "arbete", "arbetet", "arbeten", "arbeta", "arbetar", "arbetade",
            "förskola", "förskolan", "förskolor",
            "hälsa", "hälsan",
            "sjuksköterska", "sjuksköterskan", "sjuksköterskor",
            "ingenjör", "ingenjören", "ingenjörer",
            "programmerare", "projektledare",
            "ekonomi", "ekonomin", "ekonomisk",
            "chef", "chefen", "chefer",
            "undersköterska", "butik", "butiken", "butiker",
            "erfarenhet", "erfarenheter", "kunskap", "kunskaper",
            "ansvar", "ansvarig", "ledning", "ledare",
            "svenska", "svensk",
            // extra real job titles + common terms
            "systemutvecklare", "mjukvaruutvecklare", "fullstackutvecklare",
            "testare", "arkitekt", "konsult", "specialist", "tekniker",
            "administratör", "samordnare", "handläggare", "rektor",
            "förskollärare", "barnskötare", "vårdbiträde", "personlig",
            "assistent", "snickare", "elektriker", "lastbilsförare",
            "kommunikatör", "marknadsförare", "rekryterare", "controller",
            "kvalitet", "utbildning", "kompetens", "ledarskap", "service",
            "produktion", "logistik", "försäljning", "kundtjänst",
        };

        var data = new TheoryData<string>();
        foreach (var w in words)
        {
            data.Add(w);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NonStopwordCorpus))]
    public async Task Stem_AgainstPostgresOracle_MatchesSingleLexeme(string word)
    {
        var ct = TestContext.Current.CancellationToken;

        var tsvText = await ToTsvectorTextAsync(word, ct);
        // Guard: corpus must be non-stopwords (a stopword would yield empty).
        tsvText.ShouldNotBeNullOrWhiteSpace(
            $"'{word}' gav tom to_tsvector — den hör inte i non-stopword-korpusen.");
        var pgLexeme = ParseSingleLexeme(tsvText);

        var localStem = NewStemmer().Stem(word, TextLanguage.Swedish);

        localStem.ShouldBe(pgLexeme,
            $"Stemmer-drift mot PG för '{word}': lokal '{localStem}' ≠ PG '{pgLexeme}'.");
    }

    // ===============================================================
    // 2. Every embedded stopword → empty to_tsvector AND empty ToLexemes
    // ===============================================================

    [Fact]
    public async Task EmbeddedStopwords_ProduceEmptyTsvectorAndEmptyLexemes()
    {
        var ct = TestContext.Current.CancellationToken;
        var embedded = await ReadEmbeddedStopwordsAsync();
        embedded.Count.ShouldBe(114,
            "Embeddad swedish.stop ska ha exakt 114 ord (PG 18.3-paritet).");

        var analyzer = NewAnalyzer();
        var leaked = new List<string>();

        foreach (var stopword in embedded)
        {
            var tsvText = await ToTsvectorTextAsync(stopword, ct);
            if (!string.IsNullOrWhiteSpace(tsvText))
            {
                leaked.Add($"{stopword} → PG '{tsvText}'");
            }

            var lexemes = analyzer.ToLexemes(stopword, TextLanguage.Swedish);
            if (lexemes.Count != 0)
            {
                leaked.Add($"{stopword} → analyzer [{string.Join(",", lexemes)}]");
            }
        }

        leaked.ShouldBeEmpty(
            "Varje embeddat stopord ska ge tom to_tsvector OCH tom ToLexemes; " +
            $"läckage: {string.Join("; ", leaked)}");
    }

    // ===============================================================
    // 3. HARD stopword diff — PG's own list ≡ embedded swedish.stop
    // ===============================================================

    [Fact]
    public async Task EmbeddedStopwordList_EqualsPostgresBuiltInList_LineForLine()
    {
        var ct = TestContext.Current.CancellationToken;

        HashSet<string>? pgStopwords = await TryReadPgStopwordFileAsync(ct);

        if (pgStopwords is not null)
        {
            // pg_read_file available (Testcontainers connects as superuser) —
            // the strong assertion: line-set equality, every difference named.
            var embedded = await ReadEmbeddedStopwordsAsync();

            var missingFromEmbedded = pgStopwords.Except(embedded).OrderBy(w => w).ToList();
            var extraInEmbedded = embedded.Except(pgStopwords).OrderBy(w => w).ToList();

            missingFromEmbedded.ShouldBeEmpty(
                "Ord i PG:s lista men SAKNAS i embeddad swedish.stop: " +
                string.Join(", ", missingFromEmbedded));
            extraInEmbedded.ShouldBeEmpty(
                "Ord i embeddad swedish.stop men SAKNAS i PG:s lista: " +
                string.Join(", ", extraInEmbedded));
        }
        else
        {
            // Fallback when pg_read_file is unavailable: every embedded stopword
            // yields empty to_tsvector AND a curated set of clearly-non-stopwords
            // yields non-empty. (Weaker, but still falsifies gross drift.)
            var embedded = await ReadEmbeddedStopwordsAsync();
            foreach (var stopword in embedded)
            {
                (await ToTsvectorTextAsync(stopword, ct)).ShouldBeNullOrWhiteSpace(
                    $"Embeddat '{stopword}' borde vara stopord (tom to_tsvector).");
            }

            string[] clearlyNotStopwords =
                ["lärare", "utvecklare", "ingenjör", "arbete", "kunskap", "ansvar"];
            foreach (var w in clearlyNotStopwords)
            {
                (await ToTsvectorTextAsync(w, ct)).ShouldNotBeNullOrWhiteSpace(
                    $"'{w}' är inget stopord men gav tom to_tsvector.");
            }
        }
    }

    // Reads PG's own swedish.stop via pg_read_file (superuser). Returns null if
    // the function/file is unavailable (triggers the documented fallback path).
    private async Task<HashSet<string>?> TryReadPgStopwordFileAsync(CancellationToken ct)
    {
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT pg_read_file(@path)";
            cmd.Parameters.AddWithValue("path", PgStopwordPath);
            var raw = await cmd.ExecuteScalarAsync(ct);
            if (raw is not string content)
            {
                return null;
            }

            return content
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (PostgresException)
        {
            // Permission denied / file not found in this image → fallback path.
            return null;
        }
    }

    // ===============================================================
    // 4. Analyzer end-to-end parity — ToLexemes set ≡ PG distinct lexemes
    // ===============================================================

    [Theory]
    [InlineData("erfaren lärare söks till vår förskola")]
    [InlineData("vi söker en utvecklare med kunskap om system")]
    [InlineData("sjuksköterska med ansvar för hälsa och ledning")]
    [InlineData("ingenjör inom ekonomi och produktion")]
    public async Task ToLexemes_AgainstPostgresOracle_MatchesDistinctLexemeSet(string sentence)
    {
        var ct = TestContext.Current.CancellationToken;

        var tsvText = await ToTsvectorTextAsync(sentence, ct);
        var pgLexemes = ParseDistinctLexemes(tsvText);

        var local = NewAnalyzer().ToLexemes(sentence, TextLanguage.Swedish);
        var localSet = local.ToHashSet(StringComparer.Ordinal);

        // Word-token parity only (positions/weights and URL/email/number parser
        // token-classes are out of scope, CLAUDE.md §5) — compare the SETS.
        localSet.ShouldBe(pgLexemes, ignoreOrder: true,
            $"Analyzer-set ≠ PG distinct-lexem-set för: '{sentence}'. " +
            $"Lokal: [{string.Join(",", localSet.OrderBy(x => x))}] | " +
            $"PG: [{string.Join(",", pgLexemes.OrderBy(x => x))}]");
    }

    // ---------------------------------------------------------------
    // Embedded swedish.stop reader — reads the SAME asset the analyzer
    // embeds, via the Infrastructure assembly's manifest resource stream.
    // ---------------------------------------------------------------
    private static async Task<HashSet<string>> ReadEmbeddedStopwordsAsync()
    {
        var asm = typeof(SwedishTextAnalyzer).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith("swedish.stop", StringComparison.Ordinal));
        resourceName.ShouldNotBeNull(
            "swedish.stop ska vara en <EmbeddedResource> i Infrastructure-assemblyn.");

        await using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        return content
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }
}
