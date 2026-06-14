using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.TextAnalysis;

// Fas 4 STEG 2 (F4-2) — Hunspell svensk stavningskontroll (DSSO sv_SE).
//
// RED PHASE on TWO fronts:
//   1. HunspellSwedishSpellChecker does not exist yet (compile-fail).
//   2. The DSSO sv_SE.dic / sv_SE.aff Content files have not shipped yet —
//      lazy WordList load on first Check/Suggest will fail until they land
//      next to AppContext.BaseDirectory. Documented in the test-writer report.
//
// Placed in Api.IntegrationTests (not UnitTests) because it depends on the
// on-disk DSSO data files copied to the output directory — it is a real
// data-file integration, not a pure in-memory unit. No Testcontainers needed.
//
// Determinism (CLAUDE.md §5, ADR 0071): Suggest returns CANDIDATES only — the
// CV engine never silently rewrites. These tests assert candidate behaviour,
// never an applied correction.
//
// Naming: Method_Scenario_Expected.
public class SwedishSpellCheckerTests
{
    private static HunspellSwedishSpellChecker NewSpellChecker() => new();

    // ===============================================================
    // Check — correct Swedish words pass
    // ===============================================================

    [Theory]
    [InlineData("hej")]
    [InlineData("arbete")]
    [InlineData("utvecklare")]
    [InlineData("svenska")]
    [InlineData("kunskap")]
    public void Check_CorrectlySpelledSwedishWord_ReturnsTrue(string word)
    {
        NewSpellChecker().Check(word, TextLanguage.Swedish).ShouldBeTrue(
            $"'{word}' är korrekt svenska och borde passera Hunspell-kontrollen.");
    }

    // ===============================================================
    // Check — obvious misspellings fail
    // ===============================================================

    [Theory]
    [InlineData("progrm")] // missing 'a' in program
    [InlineData("utvcklare")] // missing 'e' in utvecklare
    [InlineData("xqzkwp")] // non-word noise
    public void Check_ObviousMisspelling_ReturnsFalse(string word)
    {
        NewSpellChecker().Check(word, TextLanguage.Swedish).ShouldBeFalse(
            $"'{word}' är en uppenbar felstavning och borde underkännas.");
    }

    // ===============================================================
    // Suggest — non-empty candidate list for a clear misspelling
    // ===============================================================

    [Fact]
    public void Suggest_ClearMisspelling_ReturnsNonEmptyCandidateList()
    {
        var suggestions = NewSpellChecker().Suggest("utvcklare", TextLanguage.Swedish);

        suggestions.ShouldNotBeNull();
        suggestions.ShouldBeAssignableTo<IReadOnlyList<string>>();
        suggestions.ShouldNotBeEmpty(
            "En tydlig felstavning ska ge minst en kandidat.");
    }

    [Fact]
    public void Suggest_ClearMisspelling_ContainsIntendedWord()
    {
        // At least one clear case where the intended word is among candidates.
        var suggestions = NewSpellChecker().Suggest("utvcklare", TextLanguage.Swedish);

        suggestions.ShouldContain("utvecklare",
            "Den avsedda rättningen 'utvecklare' bör finnas bland kandidaterna.");
    }

    // ===============================================================
    // Suggest — correct word: non-throwing, IReadOnlyList (Hunspell
    // semantics: empty OR excludes the word itself; both acceptable)
    // ===============================================================

    [Fact]
    public void Suggest_CorrectlySpelledWord_ReturnsNonThrowingReadOnlyList()
    {
        var suggestions = NewSpellChecker().Suggest("arbete", TextLanguage.Swedish);

        suggestions.ShouldNotBeNull();
        suggestions.ShouldBeAssignableTo<IReadOnlyList<string>>();
        // Per Hunspell semantics for a correct word: either empty, or it does
        // not echo the word itself as a "correction". Assert non-throwing +
        // that it is not proposing to "fix" a correct word to itself.
        suggestions.ShouldNotContain("arbete");
    }

    // ===============================================================
    // English fail-fast — F4-2 implements Swedish ONLY
    // ===============================================================

    [Fact]
    public void Check_EnglishLanguage_ThrowsNotSupportedException()
    {
        var checker = NewSpellChecker();

        Should.Throw<NotSupportedException>(
            () => checker.Check("developer", TextLanguage.English));
    }

    [Fact]
    public void Suggest_EnglishLanguage_ThrowsNotSupportedException()
    {
        var checker = NewSpellChecker();

        Should.Throw<NotSupportedException>(
            () => checker.Suggest("developr", TextLanguage.English));
    }
}
