using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.Api.IntegrationTests.TextAnalysis;

// Fas 4 STEG 9 (F4-9) — THE HARD GATE for the English NLP path (ADR 0074 acceptance
// criterion; structural mirror of SwedishStemmerPostgresParityTests). English CVs are
// common in Sweden and must be analysable (TextLanguage.English contract, wired here).
// Proves the local Snowball English stemmer + analyzer pipeline is byte-identical to
// PostgreSQL 18.3 to_tsvector('english'); the embedded english.stop is line-for-line
// equal to PG's built-in english stopword list. If this drifts, an English CV's lexemes
// diverge from what the matching engine + search_vector store.
//
// RED on THREE fronts (the production work CC must do for F4-9):
//   1. The F4-2 impls are renamed to the language-agnostic SnowballStemmer /
//      LocalTextAnalyzer (the bound new names) — they do not exist yet (compile-fail).
//   2. Those impls must add English support (TextLanguage.English no longer throws
//      NotSupportedException — Snowball EnglishStemmer + the english.stop set).
//   3. english.stop must ship as an <EmbeddedResource> in the Infrastructure assembly
//      (parity swedish.stop) and be byte-identical to PG 18.3's built-in list.
//
// Self-contained fixture (own PostgreSqlContainer, IAsyncLifetime) — mirrors the Swedish
// gate exactly. WORD-token parity only (URL/email/number parser token-classes out of
// scope, CLAUDE.md §5).
public sealed class EnglishStemmerPostgresParityTests : IAsyncLifetime
{
    private const string PgStopwordPath =
        "/usr/share/postgresql/18/tsearch_data/english.stop";

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
    // SUT factories — the F4-9 RENAMED, language-agnostic types.
    // ---------------------------------------------------------------
    private static SnowballStemmer NewStemmer() => new();

    private static LocalTextAnalyzer NewAnalyzer() => new(new SnowballStemmer());

    // ---------------------------------------------------------------
    // PG oracle helpers (parameterised — never string concatenation).
    // ---------------------------------------------------------------

    private async Task<string> ToTsvectorTextAsync(string word, CancellationToken ct)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT to_tsvector('english', @w)::text";
        cmd.Parameters.AddWithValue("w", word);
        return (string)(await cmd.ExecuteScalarAsync(ct))!;
    }

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
        lexeme.ShouldNotContain("':");
        return lexeme;
    }

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
                i += end + 2;
            }
            else
            {
                i++;
            }
        }

        return lexemes;
    }

    // ===============================================================
    // 1. Stemmer ≡ PG for every non-stopword in the English corpus
    // ===============================================================

    public static TheoryData<string> NonStopwordCorpus()
    {
        var words = new[]
        {
            // job titles + common CV/work terms (NON-STOPWORDS only).
            "developer", "developers", "developed", "developing",
            "engineer", "engineering", "engineered",
            "manage", "managed", "managing", "manager", "management",
            "responsible", "responsibility", "responsibilities",
            "lead", "leading", "leadership",
            "build", "building", "built",
            "deliver", "delivered", "delivery", "deliverables",
            "analyse", "analysed", "analysis", "analyst",
            "optimise", "optimised", "optimisation",
            "design", "designed", "designer",
            "implement", "implemented", "implementation",
            "communicate", "communicated", "communication",
            "system", "systems", "platform", "platforms",
            "experience", "experienced", "knowledge", "skill", "skills",
            "team", "teams", "project", "projects", "customer", "customers",
            "increase", "increased", "reduce", "reduced", "improve", "improved",
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
        tsvText.ShouldNotBeNullOrWhiteSpace(
            $"'{word}' gav tom to_tsvector — den hör inte i non-stopword-korpusen.");
        var pgLexeme = ParseSingleLexeme(tsvText);

        var localStem = NewStemmer().Stem(word, TextLanguage.English);

        localStem.ShouldBe(pgLexeme,
            $"Engelsk stemmer-drift mot PG för '{word}': lokal '{localStem}' ≠ PG '{pgLexeme}'.");
    }

    // ===============================================================
    // 2. Every embedded english stopword → empty to_tsvector AND empty ToLexemes
    // ===============================================================

    [Fact]
    public async Task EmbeddedStopwords_ProduceEmptyTsvectorAndEmptyLexemes()
    {
        var ct = TestContext.Current.CancellationToken;
        var embedded = await ReadEmbeddedStopwordsAsync();
        embedded.ShouldNotBeEmpty("Embeddad english.stop ska finnas och vara icke-tom.");

        var analyzer = NewAnalyzer();
        var leaked = new List<string>();

        foreach (var stopword in embedded)
        {
            var tsvText = await ToTsvectorTextAsync(stopword, ct);
            if (!string.IsNullOrWhiteSpace(tsvText))
            {
                leaked.Add($"{stopword} → PG '{tsvText}'");
            }

            var lexemes = analyzer.ToLexemes(stopword, TextLanguage.English);
            if (lexemes.Count != 0)
            {
                leaked.Add($"{stopword} → analyzer [{string.Join(",", lexemes)}]");
            }
        }

        leaked.ShouldBeEmpty(
            "Varje embeddat engelskt stopord ska ge tom to_tsvector OCH tom ToLexemes; " +
            $"läckage: {string.Join("; ", leaked)}");
    }

    // ===============================================================
    // 3. HARD stopword diff — PG's own english list ≡ embedded english.stop
    // ===============================================================

    [Fact]
    public async Task EmbeddedStopwordList_EqualsPostgresBuiltInList_LineForLine()
    {
        var ct = TestContext.Current.CancellationToken;

        HashSet<string>? pgStopwords = await TryReadPgStopwordFileAsync(ct);

        if (pgStopwords is not null)
        {
            var embedded = await ReadEmbeddedStopwordsAsync();

            var missingFromEmbedded = pgStopwords.Except(embedded).OrderBy(w => w).ToList();
            var extraInEmbedded = embedded.Except(pgStopwords).OrderBy(w => w).ToList();

            missingFromEmbedded.ShouldBeEmpty(
                "Ord i PG:s english-lista men SAKNAS i embeddad english.stop: " +
                string.Join(", ", missingFromEmbedded));
            extraInEmbedded.ShouldBeEmpty(
                "Ord i embeddad english.stop men SAKNAS i PG:s english-lista: " +
                string.Join(", ", extraInEmbedded));
        }
        else
        {
            var embedded = await ReadEmbeddedStopwordsAsync();
            foreach (var stopword in embedded)
            {
                (await ToTsvectorTextAsync(stopword, ct)).ShouldBeNullOrWhiteSpace(
                    $"Embeddat '{stopword}' borde vara stopord (tom to_tsvector).");
            }

            string[] clearlyNotStopwords =
                ["developer", "engineer", "managed", "responsibilities", "knowledge", "platform"];
            foreach (var w in clearlyNotStopwords)
            {
                (await ToTsvectorTextAsync(w, ct)).ShouldNotBeNullOrWhiteSpace(
                    $"'{w}' är inget stopord men gav tom to_tsvector.");
            }
        }
    }

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
            return null;
        }
    }

    // ===============================================================
    // 4. Analyzer end-to-end parity — ToLexemes set ≡ PG distinct lexemes
    // ===============================================================

    [Theory]
    [InlineData("experienced developer managed a team of engineers")]
    [InlineData("responsible for building and delivering scalable systems")]
    [InlineData("led the project and improved customer satisfaction")]
    [InlineData("analysed requirements and designed the platform architecture")]
    public async Task ToLexemes_AgainstPostgresOracle_MatchesDistinctLexemeSet(string sentence)
    {
        var ct = TestContext.Current.CancellationToken;

        var tsvText = await ToTsvectorTextAsync(sentence, ct);
        var pgLexemes = ParseDistinctLexemes(tsvText);

        var local = NewAnalyzer().ToLexemes(sentence, TextLanguage.English);
        var localSet = local.ToHashSet(StringComparer.Ordinal);

        localSet.ShouldBe(pgLexemes, ignoreOrder: true,
            $"Engelsk analyzer-set ≠ PG distinct-lexem-set för: '{sentence}'. " +
            $"Lokal: [{string.Join(",", localSet.OrderBy(x => x))}] | " +
            $"PG: [{string.Join(",", pgLexemes.OrderBy(x => x))}]");
    }

    // ---------------------------------------------------------------
    // Embedded english.stop reader — reads the SAME asset the analyzer
    // embeds, via the Infrastructure assembly's manifest resource stream.
    // ---------------------------------------------------------------
    private static async Task<HashSet<string>> ReadEmbeddedStopwordsAsync()
    {
        var asm = typeof(LocalTextAnalyzer).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith("english.stop", StringComparison.Ordinal));
        resourceName.ShouldNotBeNull(
            "english.stop ska vara en <EmbeddedResource> i Infrastructure-assemblyn (F4-9).");

        await using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        return content
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }
}
