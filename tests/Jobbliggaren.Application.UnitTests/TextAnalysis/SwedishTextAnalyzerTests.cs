using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.TextAnalysis;

// Fas 4 STEG 2 (F4-2) — svensk text-analyzer (to_tsvector('swedish')-paritet).
//
// RED PHASE: written BEFORE LocalTextAnalyzer exists. The analyzer pipeline
// is lowercase → tokenise → stopword-filter → stem, mirroring how PostgreSQL
// builds search_vector. It consumes an IStemmer (composition) and loads the
// embedded swedish.stop itself.
//
// Tokenisation precision note (CLAUDE.md §5, ADR 0074): only WORD-token parity
// is asserted here. PG's parser handling of URLs/e-mails/numbers/hyphenation is
// "not assessed v1" and deliberately out of scope — these tests use plain
// whitespace/punctuation-separated Swedish word tokens only.
//
// Naming: Method_Scenario_Expected.
public class SwedishTextAnalyzerTests
{
    private static LocalTextAnalyzer NewAnalyzer()
        => new(new SnowballStemmer());

    // ===============================================================
    // Lowercasing — to_tsvector lowercases before stemming
    // ===============================================================

    [Theory]
    [InlineData("Lärare")]
    [InlineData("LÄRARE")]
    [InlineData("lärare")]
    [InlineData("LäRaRe")]
    public void ToLexemes_MixedCaseSingleWord_LowercasesBeforeStemming(string input)
    {
        var lexemes = NewAnalyzer().ToLexemes(input, TextLanguage.Swedish);

        lexemes.ShouldBe(["lär"]);
    }

    // ===============================================================
    // Stopword filtering — embedded swedish.stop dropped entirely
    // ===============================================================

    [Fact]
    public void ToLexemes_OnlyStopwords_ReturnsEmpty()
    {
        // "och att i" — all three are in the 114-word embedded list, so
        // to_tsvector produces an empty vector; the analyzer mirrors that.
        var lexemes = NewAnalyzer().ToLexemes("och att i", TextLanguage.Swedish);

        lexemes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("och")]
    [InlineData("att")]
    [InlineData("i")]
    [InlineData("en")]
    [InlineData("det")]
    [InlineData("som")]
    [InlineData("för")]
    [InlineData("är")]
    [InlineData("med")]
    [InlineData("på")]
    [InlineData("inte")]
    [InlineData("jag")]
    public void ToLexemes_SingleStopword_IsDropped(string stopword)
    {
        var lexemes = NewAnalyzer().ToLexemes(stopword, TextLanguage.Swedish);

        lexemes.ShouldBeEmpty();
    }

    [Fact]
    public void ToLexemes_StopwordsInterleavedWithContentWords_DropsOnlyStopwords()
    {
        // "jag är en lärare" → only "lärare" survives → ["lär"].
        var lexemes = NewAnalyzer().ToLexemes("jag är en lärare", TextLanguage.Swedish);

        lexemes.ShouldBe(["lär"]);
    }

    // ===============================================================
    // Multi-word — order preserved, each token stemmed
    // ===============================================================

    [Fact]
    public void ToLexemes_MultipleContentWords_PreservesOrderAndStemsEach()
    {
        // PG to_tsvector('swedish') truth (verified this session): bare "erfaren"
        // → 'erf' (the compound "erfarenhet" stems to 'erfaren' via a different
        // Snowball rule — see the golden vectors). The analyzer must match PG.
        var lexemes = NewAnalyzer().ToLexemes("erfaren lärare", TextLanguage.Swedish);

        lexemes.ShouldBe(["erf", "lär"]);
    }

    [Fact]
    public void ToLexemes_PreservesDuplicateLexemes_MatchingToTsvectorPositionSemantics()
    {
        // to_tsvector keeps repeated lexemes (positions), so the analyzer must
        // NOT silently de-duplicate. "lärare läraren" both stem to "lär".
        var lexemes = NewAnalyzer().ToLexemes("lärare läraren", TextLanguage.Swedish);

        lexemes.ShouldBe(["lär", "lär"]);
    }

    // ===============================================================
    // Punctuation / whitespace tokenisation (word-token parity only)
    // ===============================================================

    [Fact]
    public void ToLexemes_CommaAndPeriodSeparators_TokenisesOnNonWordChars()
    {
        // "lärare, läraren." → strip punctuation → two word tokens → ["lär","lär"].
        var lexemes = NewAnalyzer().ToLexemes("lärare, läraren.", TextLanguage.Swedish);

        lexemes.ShouldBe(["lär", "lär"]);
    }

    [Theory]
    [InlineData("lärare   läraren")] // collapsed multiple spaces
    [InlineData("lärare\tläraren")] // tab
    [InlineData("lärare\nläraren")] // newline
    [InlineData("  lärare läraren  ")] // leading/trailing whitespace
    public void ToLexemes_VariedWhitespace_ProducesNoEmptyTokens(string input)
    {
        var lexemes = NewAnalyzer().ToLexemes(input, TextLanguage.Swedish);

        lexemes.ShouldBe(["lär", "lär"]);
        lexemes.ShouldNotContain(string.Empty);
    }

    // ===============================================================
    // Empty / whitespace-only input → empty result
    // ===============================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData(",.;:-")] // punctuation only, no word tokens
    public void ToLexemes_EmptyOrPunctuationOnly_ReturnsEmpty(string input)
    {
        var lexemes = NewAnalyzer().ToLexemes(input, TextLanguage.Swedish);

        lexemes.ShouldBeEmpty();
    }

    // ===============================================================
    // åäö encoding survives the full pipeline
    // ===============================================================

    [Fact]
    public void ToLexemes_DiacriticBearingSentence_PreservesAaoThroughPipeline()
    {
        // "Förskola och hälsa" → "och" dropped (stopword) → ["förskol","häls"].
        var lexemes = NewAnalyzer().ToLexemes("Förskola och hälsa", TextLanguage.Swedish);

        lexemes.ShouldBe(["förskol", "häls"]);
    }

    // ===============================================================
    // English analysis — wired in F4-9 (ADR 0074 F4-2 amendment). No longer
    // throws; full to_tsvector('english') parity is EnglishStemmerPostgresParityTests.
    // ===============================================================

    [Fact]
    public void ToLexemes_EnglishLanguage_AnalysesViaSnowball_F49()
    {
        var analyzer = NewAnalyzer();

        // "experience" is not an English stopword → survives; "experienced" stems alike.
        var lexemes = analyzer.ToLexemes("developer experience", TextLanguage.English);

        lexemes.ShouldNotBeEmpty();
    }
}
