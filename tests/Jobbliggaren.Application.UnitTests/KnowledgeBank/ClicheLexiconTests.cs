using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4 STEG 7 (F4-7) — the committed cliché lexicon (cliche-list.v1.json) loads
/// through the real <see cref="ClicheLexicon"/>. The cliché list is VERSIONED DATA
/// (CLAUDE.md §5: "cliché lists ... versioned data/config per the knowledge bank, not
/// inline strings"). Every entry must carry the diagnosis (Why) + the constructive
/// alternative (BetterAlternative) so the determinism can CITE evidence and PROPOSE a
/// fix, never just flag.
///
/// Drift-robust (mirrors TaxonomySnapshotSeederTests): assert a `>=` floor + per-entry
/// structural completeness, NOT a brittle exact total (the lexicon grows as authoring
/// continues).
///
/// RED until ClicheLexicon ships internal sealed in Jobbliggaren.Infrastructure.KnowledgeBank.
/// </summary>
public class ClicheLexiconTests
{
    private static ClicheList LoadList() => new ClicheLexicon().GetClicheList();

    [Fact]
    public void GetClicheList_ShouldLoadVersionedEmbeddedResource_WhenCalled()
    {
        var list = LoadList();

        list.ShouldNotBeNull();
        list.Version.ShouldNotBeNullOrWhiteSpace();
        // Sentinel guard — the committed list must bear a real version, not the
        // loader's default fallback.
        list.Version.ShouldNotBe("unknown");
    }

    [Fact]
    public void GetClicheList_ShouldContainAtLeastTwelveEntries_WhenCalled()
    {
        // Drift-robust floor (architect: >=12). Asserted as `>=`, not `==`, so the
        // lexicon can grow without breaking the test.
        var list = LoadList();

        list.Entries.Count.ShouldBeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void GetClicheList_ShouldHaveAllThreeFieldsNonEmptyOnEveryEntry_WhenCalled()
    {
        // Every entry diagnoses (Why) AND proposes (BetterAlternative) — the
        // propose-and-explain contract. An entry with an empty Why or
        // BetterAlternative would be a flag-without-help (rejected by CLAUDE.md §5).
        var list = LoadList();

        list.Entries.ShouldAllBe(e =>
            !string.IsNullOrWhiteSpace(e.Phrase)
            && !string.IsNullOrWhiteSpace(e.Why)
            && !string.IsNullOrWhiteSpace(e.BetterAlternative));
    }

    [Fact]
    public void GetClicheList_ShouldHaveUniquePhrases_WhenCalled()
    {
        // No duplicate cliché phrase (case-insensitive) — a duplicate would double-flag
        // the same span and bloat the verdict.
        var list = LoadList();

        var phrases = list.Entries
            .Select(e => e.Phrase.Trim().ToLowerInvariant())
            .ToList();
        phrases.Distinct().Count().ShouldBe(phrases.Count);
    }

    [Fact]
    public void GetClicheList_ShouldNotSuggestThePhraseAsItsOwnAlternative_WhenCalled()
    {
        // The BetterAlternative must actually differ from the flagged Phrase — a
        // tautological "replace X with X" entry would be useless guidance.
        var list = LoadList();

        list.Entries.ShouldAllBe(e =>
            !string.Equals(
                e.Phrase.Trim(),
                e.BetterAlternative.Trim(),
                StringComparison.OrdinalIgnoreCase));
    }
}
